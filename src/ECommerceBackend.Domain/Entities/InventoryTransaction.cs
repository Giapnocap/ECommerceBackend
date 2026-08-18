using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class InventoryTransaction
    {
        internal InventoryTransaction()
        {
        }

        public Guid Id { get; internal set; }
        public Guid ProductId { get; internal set; }
        public Guid? OrderId { get; internal set; }
        public Guid? CreatedByUserId { get; internal set; }
        public InventoryTransactionType Type { get; internal set; }
        public int QuantityChange { get; internal set; }
        public int BalanceAfter { get; internal set; }
        public string? Reference { get; internal set; }
        public string? Reason { get; internal set; }
        public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;

        public Product? Product { get; set; }
        public Order? Order { get; set; }
        public User? CreatedByUser { get; set; }

        public static InventoryTransaction Create(
            Guid id,
            Guid productId,
            Guid? orderId,
            Guid? createdByUserId,
            InventoryTransactionType type,
            InventoryMutation mutation,
            string? reason,
            DateTime createdAt,
            string? reference = null)
        {
            if (id == Guid.Empty || productId == Guid.Empty
                || createdByUserId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "inventory_transaction_identity_invalid",
                    "Thông tin định danh của giao dịch tồn kho không hợp lệ.");
            }

            if (!Enum.IsDefined(type)
                || mutation.QuantityChange == 0
                || mutation.BalanceAfter < 0
                || !MatchesTransactionType(type, orderId, mutation.QuantityChange))
            {
                throw new DomainRuleViolationException(
                    "inventory_transaction_invalid",
                    "Biến động tồn kho không phù hợp với loại giao dịch.");
            }

            if (reason?.Trim().Length > 500)
            {
                throw new DomainRuleViolationException(
                    "inventory_transaction_reason_invalid",
                    "Lý do biến động tồn kho không được vượt quá 500 ký tự.");
            }

            if (reference?.Trim().Length > 200)
            {
                throw new DomainRuleViolationException(
                    "inventory_transaction_reference_invalid",
                    "Mã tham chiếu biến động tồn kho không được vượt quá 200 ký tự.");
            }

            return new InventoryTransaction
            {
                Id = id,
                ProductId = productId,
                OrderId = orderId,
                CreatedByUserId = createdByUserId,
                Type = type,
                QuantityChange = mutation.QuantityChange,
                BalanceAfter = mutation.BalanceAfter,
                Reference = string.IsNullOrWhiteSpace(reference)
                    ? null
                    : reference.Trim(),
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? null
                    : reason.Trim(),
                CreatedAt = createdAt
            };
        }

        private static bool MatchesTransactionType(
            InventoryTransactionType type,
            Guid? orderId,
            int quantityChange)
            => type switch
            {
                InventoryTransactionType.InitialStock =>
                    !orderId.HasValue && quantityChange > 0,
                InventoryTransactionType.StockIn =>
                    !orderId.HasValue && quantityChange > 0,
                InventoryTransactionType.ManualAdjustment =>
                    !orderId.HasValue,
                InventoryTransactionType.OrderPlaced =>
                    orderId.HasValue && quantityChange < 0,
                InventoryTransactionType.OrderCancelled
                    or InventoryTransactionType.OrderReturned =>
                    orderId.HasValue && quantityChange > 0,
                _ => false
            };
    }
}
