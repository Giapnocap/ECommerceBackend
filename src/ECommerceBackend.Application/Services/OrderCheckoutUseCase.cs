using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderCheckoutUseCase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly CheckoutCartLoader _cartLoader;
        private readonly CheckoutOrderFactory _orderFactory;
        private readonly CheckoutOrderWriter _orderWriter;
        private readonly OrderPricingUseCase _pricing;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;
        private readonly OrderLifecycleOptions _options;

        public OrderCheckoutUseCase(
            IOrderRepository orderRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            CheckoutCartLoader cartLoader,
            CheckoutOrderFactory orderFactory,
            CheckoutOrderWriter orderWriter,
            OrderPricingUseCase pricing,
            OrderQueryUseCase queries,
            TimeProvider timeProvider,
            IOptions<OrderLifecycleOptions> options)
        {
            _orderRepository = orderRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _cartLoader = cartLoader;
            _orderFactory = orderFactory;
            _orderWriter = orderWriter;
            _pricing = pricing;
            _queries = queries;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var paymentMethod = Enum.IsDefined(request.PaymentMethod)
                ? request.PaymentMethod.ToString()
                : "unknown";
            using var telemetry = BusinessTelemetry.Start(
                "checkout.place_order",
                cancellationToken,
                new KeyValuePair<string, object?>(
                    "payment.method",
                    paymentMethod));
            var normalizedKey =
                CheckoutRequestIdentity.NormalizeKey(idempotencyKey);
            var requestHash = CheckoutRequestIdentity.Hash(request);
            var existingOrder = await FindIdempotentOrderAsync(
                userId,
                normalizedKey,
                cancellationToken);
            if (existingOrder != null)
            {
                CheckoutRequestIdentity.EnsureSameRequest(
                    existingOrder,
                    requestHash);
                telemetry.SetTag("checkout.idempotency.replay", true);
                var replay = await GetResponseAsync(
                    existingOrder.Id,
                    userId,
                    cancellationToken);
                telemetry.Complete();
                return replay;
            }

            Guid orderId;
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;
            try
            {
                var cart = await _cartLoader.LockAsync(
                    userId,
                    cancellationToken);
                existingOrder = await FindIdempotentOrderAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (existingOrder != null)
                {
                    CheckoutRequestIdentity.EnsureSameRequest(
                        existingOrder,
                        requestHash);
                    orderId = existingOrder.Id;
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    telemetry.SetTag("checkout.idempotency.replay", true);
                    var replay = await GetResponseAsync(
                        orderId,
                        userId,
                        cancellationToken);
                    telemetry.Complete();
                    return replay;
                }

                var pendingOrderCount =
                    await _orderRepository.CountPendingByUserAsync(
                    userId,
                    cancellationToken);
                if (pendingOrderCount
                    >= _options.MaxPendingOrdersPerCustomer)
                {
                    throw new ConflictException(
                        "pending_order_limit_reached",
                        $"Bạn chỉ có thể có tối đa "
                        + $"{_options.MaxPendingOrdersPerCustomer} "
                        + "đơn hàng đang chờ xử lý.");
                }

                await _cartLoader.LoadItemsAsync(
                    cart,
                    cancellationToken);

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var pricing = await _pricing.CalculateForCheckoutAsync(
                    userId,
                    cart.CartItems,
                    request.PromotionCode,
                    request.ShippingMethod,
                    occurredAt,
                    cancellationToken);
                if (request.ExpectedTotalAmount.HasValue
                    && request.ExpectedTotalAmount.Value
                        != pricing.Amounts.Total)
                {
                    throw new ConflictException(
                        "checkout_price_changed",
                        "Tổng tiền đã thay đổi. Vui lòng tải lại báo giá trước khi đặt hàng.");
                }
                var creation = _orderFactory.Create(
                    userId,
                    request,
                    normalizedKey,
                    requestHash,
                    pricing,
                    occurredAt);
                var order = creation.Order;
                var payment = creation.Payment;
                orderId = order.Id;

                _orderWriter.AddRecords(
                    order,
                    payment,
                    cart,
                    userId,
                    occurredAt);
                await _pricing.RedeemAsync(
                    pricing,
                    order,
                    userId,
                    occurredAt,
                    cancellationToken);
                _outbox.EnqueueNotification(
                    userId,
                    "Đặt hàng thành công",
                    $"Đơn hàng {order.OrderNumber} đã được tiếp nhận "
                    + "và đang chờ xác nhận.",
                    order.Id);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException(
                    "Tồn kho vừa được thay đổi. Vui lòng tải lại "
                    + "giỏ hàng và thử lại.",
                    ex);
            }
            catch (Exception ex)
                when (_consistency.IsUniqueConstraintViolation(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                var savedOrder = await FindIdempotentOrderAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (savedOrder != null)
                {
                    CheckoutRequestIdentity.EnsureSameRequest(
                        savedOrder,
                        requestHash);
                    telemetry.SetTag(
                        "checkout.idempotency.replay",
                        true);
                    var replay = await GetResponseAsync(
                        savedOrder.Id,
                        userId,
                        cancellationToken);
                    telemetry.Complete();
                    return replay;
                }
                throw new ConflictException(
                    "Không thể tạo đơn hàng do dữ liệu "
                    + "vừa được cập nhật.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên "
                    + "cùng sản phẩm. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            var response = await GetResponseAsync(
                orderId,
                userId,
                cancellationToken);
            telemetry.Complete();
            return response;
        }

        private async Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid userId,
            CancellationToken cancellationToken)
            => await _queries.GetByIdAsync(
                orderId,
                userId,
                canProcessOrders: true,
                cancellationToken);

        private async Task<Order?> FindIdempotentOrderAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken)
            => await _orderRepository.FindByIdempotencyKeyAsync(
                userId,
                idempotencyKey,
                cancellationToken);

    }
}
