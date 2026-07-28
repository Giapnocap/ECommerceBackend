using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderReturnUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;
        private readonly ReturnPolicyOptions _options;

        public OrderReturnUseCase(
            IFulfillmentRepository fulfillmentRepository,
            IOrderRepository orderRepository,
            IInventoryRepository inventoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            IAuditWriter audit,
            OrderQueryUseCase queries,
            TimeProvider timeProvider,
            IOptions<ReturnPolicyOptions> options)
        {
            _fulfillmentRepository = fulfillmentRepository;
            _orderRepository = orderRepository;
            _inventoryRepository = inventoryRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _audit = audit;
            _queries = queries;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<OrderResponse> RequestAsync(
            Guid orderId,
            Guid customerUserId,
            CreateReturnRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginAsync(cancellationToken);
            var completed = false;
            try
            {
                var order = await _consistency.LockOrderAsync(
                    orderId,
                    cancellationToken);
                if (order == null || order.UserId != customerUserId)
                    throw new NotFoundException("Không tìm thấy đơn hàng.");

                var existing =
                    await _fulfillmentRepository.LockReturnRequestByOrderIdAsync(
                        orderId,
                        cancellationToken);
                if (existing != null)
                {
                    if (!string.Equals(
                        existing.Reason,
                        request.Reason.Trim(),
                        StringComparison.Ordinal))
                    {
                        throw new ConflictException(
                            "return_request_already_exists",
                            "Đơn hàng đã có một yêu cầu trả hàng khác.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (order.Status != OrderStatus.Delivered)
                    {
                        throw new ConflictException(
                            "return_request_status_invalid",
                            "Chỉ có thể yêu cầu trả đơn đã giao thành công.");
                    }

                    var shipment =
                        await _fulfillmentRepository.LockShipmentByOrderIdAsync(
                            orderId,
                            cancellationToken);
                    if (shipment?.DeliveredAt == null)
                    {
                        throw new ConflictException(
                            "return_delivery_time_missing",
                            "Đơn hàng chưa có thời điểm giao thành công.");
                    }

                    var occurredAt = UtcNow;
                    if (occurredAt
                        > shipment.DeliveredAt.Value.AddDays(
                            _options.ReturnWindowDays))
                    {
                        throw new ConflictException(
                            "return_window_expired",
                            $"Đơn hàng đã quá thời hạn trả hàng {_options.ReturnWindowDays} ngày.");
                    }

                    var payment = await _consistency.LockPaymentByOrderIdAsync(
                        orderId,
                        cancellationToken)
                        ?? throw new ConflictException(
                            "order_payment_missing",
                            "Đơn hàng không có giao dịch thanh toán.");
                    var returnRequest = DomainRuleGuard.AsBusiness(() =>
                        ReturnRequest.Create(
                            Guid.NewGuid(),
                            order.Id,
                            customerUserId,
                            request.Reason,
                            occurredAt));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.ReturnRequested,
                            payment.Status));
                    _fulfillmentRepository.AddReturnRequest(returnRequest);
                    AddHistory(
                        order,
                        statusChange,
                        customerUserId,
                        returnRequest.Reason,
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đã tiếp nhận yêu cầu trả hàng",
                        $"Yêu cầu trả đơn {order.OrderNumber} đang chờ xét duyệt.",
                        order.Id,
                        payment.Id);
                    WriteAudit(
                        "return.request",
                        returnRequest,
                        customerUserId,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = returnRequest.Reason
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch
            {
                await RollbackAsync(transaction, completed);
                throw;
            }

            return await GetResponseAsync(
                orderId,
                customerUserId,
                false,
                cancellationToken);
        }

        public async Task<OrderResponse> ReviewAsync(
            Guid orderId,
            Guid actorUserId,
            ReviewReturnRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginAsync(cancellationToken);
            var completed = false;
            try
            {
                var order = await LockOrderAsync(orderId, cancellationToken);
                var returnRequest = await LockReturnAsync(
                    orderId,
                    cancellationToken);
                var expectedStatus = request.Decision == ReturnReviewDecision.Approve
                    ? ReturnRequestStatus.Approved
                    : ReturnRequestStatus.Rejected;

                if (returnRequest.Status == expectedStatus)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (returnRequest.Status != ReturnRequestStatus.Pending
                        || order.Status != OrderStatus.ReturnRequested)
                    {
                        throw new ConflictException(
                            "return_review_status_invalid",
                            "Yêu cầu trả hàng không còn ở trạng thái chờ xét duyệt.");
                    }

                    var payment = await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken);
                    var occurredAt = UtcNow;
                    DomainRuleGuard.AsBusiness(() =>
                        returnRequest.Review(
                            request.Decision,
                            actorUserId,
                            occurredAt,
                            request.Note));
                    var nextOrderStatus =
                        request.Decision == ReturnReviewDecision.Approve
                            ? OrderStatus.ReturnApproved
                            : OrderStatus.Delivered;
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            nextOrderStatus,
                            payment?.Status));
                    AddHistory(
                        order,
                        statusChange,
                        actorUserId,
                        NormalizeOptional(request.Note),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        request.Decision == ReturnReviewDecision.Approve
                            ? "Yêu cầu trả hàng đã được duyệt"
                            : "Yêu cầu trả hàng bị từ chối",
                        request.Decision == ReturnReviewDecision.Approve
                            ? $"Bạn có thể gửi trả đơn {order.OrderNumber}."
                            : $"Yêu cầu trả đơn {order.OrderNumber} không được chấp nhận.",
                        order.Id,
                        payment?.Id);
                    WriteAudit(
                        "return.review",
                        returnRequest,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["decision"] = request.Decision.ToString(),
                            ["note"] = NormalizeOptional(request.Note)
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch
            {
                await RollbackAsync(transaction, completed);
                throw;
            }

            return await GetResponseAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);
        }

        public async Task<OrderResponse> ReceiveAsync(
            Guid orderId,
            Guid actorUserId,
            ReceiveReturnRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await BeginAsync(cancellationToken);
            var completed = false;
            try
            {
                var order = await LockOrderAsync(orderId, cancellationToken);
                var returnRequest = await LockReturnAsync(
                    orderId,
                    cancellationToken);

                if ((returnRequest.Status is ReturnRequestStatus.Received
                    or ReturnRequestStatus.Refunded)
                    && (order.Status is OrderStatus.Returned
                        or OrderStatus.Refunded))
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (returnRequest.Status != ReturnRequestStatus.Approved
                        || order.Status != OrderStatus.ReturnApproved)
                    {
                        throw new ConflictException(
                            "return_receive_status_invalid",
                            "Chỉ có thể nhận hàng hoàn của yêu cầu đã được duyệt.");
                    }

                    var payment = await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken);
                    var occurredAt = UtcNow;
                    DomainRuleGuard.AsBusiness(() =>
                        returnRequest.Receive(
                            actorUserId,
                            occurredAt,
                            request.InspectionNote));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Returned,
                            payment?.Status));
                    await RestoreStockAsync(
                        order,
                        actorUserId,
                        occurredAt,
                        cancellationToken);
                    AddHistory(
                        order,
                        statusChange,
                        actorUserId,
                        request.InspectionNote.Trim(),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đã nhận hàng hoàn",
                        $"Đơn hàng {order.OrderNumber} đã được nhận lại và đang chờ hoàn tiền.",
                        order.Id,
                        payment?.Id);
                    WriteAudit(
                        "return.receive",
                        returnRequest,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["inspectionNote"] = request.InspectionNote.Trim()
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await RollbackAsync(transaction, completed);
                throw ReturnConflict(ex);
            }
            catch
            {
                await RollbackAsync(transaction, completed);
                throw;
            }

            return await GetResponseAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        private Task<IAppTransaction> BeginAsync(
            CancellationToken cancellationToken)
            => _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        private async Task<Order> LockOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken)
            => await _consistency.LockOrderAsync(orderId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        private async Task<ReturnRequest> LockReturnAsync(
            Guid orderId,
            CancellationToken cancellationToken)
            => await _fulfillmentRepository.LockReturnRequestByOrderIdAsync(
                orderId,
                cancellationToken)
                ?? throw new NotFoundException(
                    "Không tìm thấy yêu cầu trả hàng.");

        private async Task RestoreStockAsync(
            Order order,
            Guid actorUserId,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            await _orderRepository.LoadDetailsAsync(order, cancellationToken);
            var products = new Dictionary<Guid, Product>();
            foreach (var productId in order.OrderDetails
                .Select(detail => detail.ProductId)
                .Distinct()
                .OrderBy(id => id))
            {
                products[productId] =
                    await _consistency.LockProductAsync(
                        productId,
                        activeOnly: false,
                        cancellationToken)
                    ?? throw new ConflictException(
                        "return_product_missing",
                        "Sản phẩm của đơn hàng không còn tồn tại.");
            }

            foreach (var detail in order.OrderDetails)
            {
                var product = products[detail.ProductId];
                var mutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.Release(product, detail.Quantity));
                _inventoryRepository.AddTransaction(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    OrderId = order.Id,
                    CreatedByUserId = actorUserId,
                    Type = InventoryTransactionType.OrderReturned,
                    QuantityChange = mutation.QuantityChange,
                    BalanceAfter = mutation.BalanceAfter,
                    Reason = $"Nhận hàng hoàn của đơn {order.OrderNumber}",
                    CreatedAt = occurredAt
                });
            }
        }

        private void AddHistory(
            Order order,
            StatusChange<OrderStatus> change,
            Guid actorUserId,
            string? note,
            DateTime occurredAt)
            => _orderRepository.AddStatusHistory(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ChangedByUserId = actorUserId,
                FromStatus = change.Previous,
                ToStatus = change.Current,
                Note = note,
                CreatedAt = occurredAt
            });

        private void WriteAudit(
            string action,
            ReturnRequest returnRequest,
            Guid actorUserId,
            IReadOnlyDictionary<string, object?> metadata)
            => _audit.Write(
                action,
                "ReturnRequest",
                returnRequest.Id.ToString(),
                actorUserId,
                metadata);

        private Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken)
            => _queries.GetByIdAsync(
                orderId,
                userId,
                canProcessOrders,
                cancellationToken);

        private static ConflictException ReturnConflict(Exception inner)
            => new(
                "return_processing_conflict",
                "Yêu cầu trả hàng vừa được xử lý bởi một thao tác khác. Vui lòng tải lại.",
                inner);

        private static async Task RollbackAsync(
            IAppTransaction transaction,
            bool completed)
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
