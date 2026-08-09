using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderReturnWorkflow
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly OrderQueryUseCase _queries;

        public OrderReturnWorkflow(
            IOrderRepository orderRepository,
            IInventoryRepository inventoryRepository,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            OrderQueryUseCase queries)
        {
            _orderRepository = orderRepository;
            _inventoryRepository = inventoryRepository;
            _consistency = consistency;
            _audit = audit;
            _queries = queries;
        }

        internal async Task RestoreStockAsync(
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
                _inventoryRepository.AddTransaction(
                    DomainRuleGuard.AsBusiness(() =>
                        InventoryTransaction.Create(
                            Guid.NewGuid(),
                            product.Id,
                            order.Id,
                            actorUserId,
                            InventoryTransactionType.OrderReturned,
                            mutation,
                            $"Nhận hàng hoàn của đơn {order.OrderNumber}",
                            occurredAt)));
            }
        }

        internal void AddHistory(
            Order order,
            StatusChange<OrderStatus> change,
            Guid actorUserId,
            string? note,
            DateTime occurredAt)
        {
            _orderRepository.AddStatusHistory(
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ChangedByUserId = actorUserId,
                    FromStatus = change.Previous,
                    ToStatus = change.Current,
                    Note = note,
                    CreatedAt = occurredAt
                });
        }

        internal void WriteAudit(
            string action,
            ReturnRequest returnRequest,
            Guid actorUserId,
            IReadOnlyDictionary<string, object?> metadata)
        {
            _audit.Write(
                action,
                nameof(ReturnRequest),
                returnRequest.Id.ToString(),
                actorUserId,
                metadata);
        }

        internal Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken)
            => _queries.GetByIdAsync(
                orderId,
                userId,
                canProcessOrders,
                cancellationToken);

        internal static ConflictException ProcessingConflict(Exception inner)
            => new(
                "return_processing_conflict",
                "Yêu cầu trả hàng vừa được xử lý bởi một thao tác khác. Vui lòng tải lại.",
                inner);

        internal static async Task RollbackAsync(
            IAppTransaction transaction,
            bool completed)
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }

        internal static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
