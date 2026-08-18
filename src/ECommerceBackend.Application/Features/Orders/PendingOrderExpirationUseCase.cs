using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class PendingOrderExpirationUseCase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly OrderCancellationWorkflow _cancellation;

        public PendingOrderExpirationUseCase(
            IOrderRepository orderRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            OrderCancellationWorkflow cancellation)
        {
            _orderRepository = orderRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _cancellation = cancellation;
        }

        public async Task<IReadOnlyList<Guid>> GetDueOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
                return Array.Empty<Guid>();

            return await _orderRepository.GetDuePendingOrderIdsAsync(
                asOf,
                batchSize,
                cancellationToken);
        }

        public async Task<bool> ExecuteAsync(
            Guid orderId,
            DateTime asOf,
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
                if (order == null
                    || order.Status != OrderStatus.Pending
                    || !order.ExpiresAt.HasValue
                    || order.ExpiresAt.Value > asOf)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return false;
                }

                var payment =
                    await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken);
                if (payment?.HasActiveExternalTransaction == true)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return false;
                }
                var statusChange = DomainRuleGuard.AsConflict(() =>
                    order.Cancel(
                        asOf,
                        payment?.Status,
                        "SystemExpired",
                        isExpiration: true));

                await _cancellation.RestoreStockAsync(
                    order,
                    null,
                    asOf,
                    InventoryTransactionType.OrderCancelled,
                    cancellationToken);
                _cancellation.UpdatePayment(
                    payment,
                    OrderStatus.Cancelled,
                    null,
                    asOf);
                _cancellation.AddHistory(
                    order,
                    statusChange,
                    null,
                    "SystemExpired",
                    asOf);
                _cancellation.EnqueueNotification(
                    order,
                    payment,
                    "Đơn hàng đã hết thời gian giữ tồn kho và được hủy tự động.");

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                return true;
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }
    }
}
