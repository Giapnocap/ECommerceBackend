using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class CustomerOrderCancellationUseCase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly OrderCancellationWorkflow _cancellation;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;

        public CustomerOrderCancellationUseCase(
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            OrderCancellationWorkflow cancellation,
            OrderQueryUseCase queries,
            TimeProvider timeProvider)
        {
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _cancellation = cancellation;
            _queries = queries;
            _timeProvider = timeProvider;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid customerUserId,
            CancelOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(
                    orderId,
                    cancellationToken);
                if (order == null || order.UserId != customerUserId)
                {
                    throw new NotFoundException(
                        "Không tìm thấy đơn hàng.");
                }

                if (order.Status == OrderStatus.Cancelled)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return await _queries.GetByIdAsync(
                        orderId,
                        customerUserId,
                        false,
                        cancellationToken);
                }

                if (order.Status != OrderStatus.Pending)
                {
                    throw new ConflictException(
                        "customer_order_cancellation_forbidden",
                        "Khách hàng chỉ có thể hủy đơn hàng đang chờ xử lý.");
                }

                var payment =
                    await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken);
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var statusChange = DomainRuleGuard.AsConflict(() =>
                    order.Cancel(
                        occurredAt,
                        payment?.Status,
                        OrderCommandRules.NormalizeOptional(request.Reason)
                        ?? "Khách hàng yêu cầu hủy"));

                await _cancellation.RestoreStockAsync(
                    order,
                    customerUserId,
                    occurredAt,
                    InventoryTransactionType.OrderCancelled,
                    cancellationToken);
                _cancellation.UpdatePayment(
                    payment,
                    OrderStatus.Cancelled,
                    customerUserId,
                    occurredAt);
                _cancellation.AddHistory(
                    order,
                    statusChange,
                    customerUserId,
                    request.Reason,
                    occurredAt);
                _cancellation.EnqueueNotification(
                    order,
                    payment,
                    "Đơn hàng đã được hủy theo yêu cầu của bạn.");

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Đơn hàng hoặc tồn kho vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await _queries.GetByIdAsync(
                orderId,
                customerUserId,
                false,
                cancellationToken);
        }
    }
}
