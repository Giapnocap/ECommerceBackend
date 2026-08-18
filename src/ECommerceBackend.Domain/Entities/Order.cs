using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class Order
    {
        internal Order()
        {
        }

        public Guid Id { get; internal set; }
        public Guid UserId { get; internal set; }
        public string OrderNumber { get; internal set; } = string.Empty;
        public string IdempotencyKey { get; internal set; } = string.Empty;
        public string IdempotencyRequestHash { get; internal set; } = string.Empty;
        public Guid? PromotionId { get; internal set; }
        public string? PromotionCodeSnapshot { get; internal set; }
        public ShippingMethod ShippingMethod { get; internal set; }
        public string Currency { get; internal set; } = "VND";
        public string BaseCurrency { get; private set; } = "VND";
        public decimal ExchangeRate { get; private set; } = 1m;
        public DateTime ExchangeRateCapturedAt { get; private set; }
        public DateTime OrderDate { get; internal set; } = DateTime.UtcNow;
        public decimal BaseSubtotalAmount { get; private set; }
        public decimal BaseDiscountAmount { get; private set; }
        public decimal BaseShippingFee { get; private set; }
        public decimal BaseTaxAmount { get; private set; }
        public decimal BaseTotalAmount { get; private set; }
        public decimal SubtotalAmount { get; private set; }
        public decimal DiscountAmount { get; private set; }
        public decimal ShippingFee { get; private set; }
        public decimal TaxAmount { get; private set; }
        public decimal TotalAmount { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatusTransitions.Initial;
        public string ShippingAddress { get; internal set; } = string.Empty;
        public string RecipientName { get; private set; } = string.Empty;
        public string? RecipientPhone { get; private set; }
        public string? Note { get; internal set; }
        public DateTime? ExpiresAt { get; private set; }
        public DateTime? CancelledAt { get; private set; }
        public DateTime? ExpiredAt { get; private set; }
        public string? CancellationReason { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public User? User { get; set; }
        public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
        public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
        public Payment? Payment { get; set; }
        public Promotion? Promotion { get; set; }
        public PromotionRedemption? PromotionRedemption { get; set; }
        public Shipment? Shipment { get; set; }
        public ReturnRequest? ReturnRequest { get; set; }

        public static Order Create(
            Guid id,
            Guid userId,
            string orderNumber,
            string idempotencyKey,
            string idempotencyRequestHash,
            Guid? promotionId,
            string? promotionCodeSnapshot,
            ShippingMethod shippingMethod,
            string currency,
            DateTime orderDate,
            string shippingAddress,
            string? note)
        {
            if (id == Guid.Empty || userId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "order_identity_invalid",
                    "Thông tin định danh của đơn hàng không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(orderNumber)
                || orderNumber.Trim().Length > 32
                || string.IsNullOrWhiteSpace(idempotencyKey)
                || idempotencyKey.Trim().Length > 100
                || idempotencyRequestHash is not { Length: 64 })
            {
                throw new DomainRuleViolationException(
                    "order_request_identity_invalid",
                    "Thông tin nhận diện yêu cầu đặt hàng không hợp lệ.");
            }

            if ((promotionId.HasValue && string.IsNullOrWhiteSpace(promotionCodeSnapshot))
                || (!promotionId.HasValue && promotionCodeSnapshot != null))
            {
                throw new DomainRuleViolationException(
                    "order_promotion_snapshot_invalid",
                    "Thông tin khuyến mãi của đơn hàng không nhất quán.");
            }

            if (!Enum.IsDefined(shippingMethod)
                || !CurrencyCatalog.IsSupported(currency))
            {
                throw new DomainRuleViolationException(
                    "order_shipping_invalid",
                    "Thông tin giao hàng hoặc tiền tệ không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(shippingAddress)
                || shippingAddress.Trim().Length > 500
                || note?.Trim().Length > 500)
            {
                throw new DomainRuleViolationException(
                    "order_delivery_details_invalid",
                    "Địa chỉ giao hàng hoặc ghi chú không hợp lệ.");
            }

            return new Order
            {
                Id = id,
                UserId = userId,
                OrderNumber = orderNumber.Trim(),
                IdempotencyKey = idempotencyKey.Trim(),
                IdempotencyRequestHash = idempotencyRequestHash,
                PromotionId = promotionId,
                PromotionCodeSnapshot = promotionCodeSnapshot?.Trim(),
                ShippingMethod = shippingMethod,
                Currency = CurrencyCatalog.Normalize(currency),
                OrderDate = orderDate,
                ShippingAddress = shippingAddress.Trim(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };
        }

        public void SetRecipient(string name, string? phone)
        {
            if (!string.IsNullOrEmpty(RecipientName))
            {
                throw new DomainRuleViolationException(
                    "order_recipient_snapshot_immutable",
                    "Thông tin người nhận của đơn hàng không thể thay đổi sau khi đã lưu.");
            }

            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            {
                throw new DomainRuleViolationException(
                    "order_recipient_name_invalid",
                    "Tên người nhận phải có từ 1 đến 100 ký tự.");
            }

            var normalizedPhone = string.IsNullOrWhiteSpace(phone)
                ? null
                : phone.Trim();
            if (normalizedPhone != null && !IsValidPhone(normalizedPhone))
            {
                throw new DomainRuleViolationException(
                    "order_recipient_phone_invalid",
                    "Số điện thoại người nhận không hợp lệ.");
            }

            RecipientName = name.Trim();
            RecipientPhone = normalizedPhone;
        }

        public void SetPricing(
            decimal subtotal,
            decimal discount,
            decimal shipping,
            decimal tax)
        {
            var amounts = OrderPricingPolicy.CalculateAmounts(
                subtotal,
                discount,
                shipping,
                tax);

            SetPricingSnapshot(
                Currency,
                1m,
                OrderDate,
                amounts,
                amounts);
        }

        public void SetPricingSnapshot(
            string baseCurrency,
            decimal exchangeRate,
            DateTime exchangeRateCapturedAt,
            OrderAmounts baseAmounts,
            OrderAmounts orderAmounts)
        {
            var normalizedBaseCurrency = CurrencyCatalog.Get(
                baseCurrency).Code;
            _ = CurrencyCatalog.Get(Currency);
            var validatedBaseAmounts = OrderPricingPolicy.CalculateAmounts(
                baseAmounts.Subtotal,
                baseAmounts.Discount,
                baseAmounts.Shipping,
                baseAmounts.Tax);
            var validatedOrderAmounts = OrderPricingPolicy.CalculateAmounts(
                orderAmounts.Subtotal,
                orderAmounts.Discount,
                orderAmounts.Shipping,
                orderAmounts.Tax);

            if (exchangeRate <= 0
                || exchangeRate > 1_000_000m
                || decimal.Round(
                    exchangeRate,
                    10,
                    MidpointRounding.ToEven) != exchangeRate
                || exchangeRateCapturedAt
                    > OrderDate.AddMinutes(5))
            {
                throw new DomainRuleViolationException(
                    "order_exchange_rate_invalid",
                    "Snapshot tỷ giá của đơn hàng không hợp lệ.");
            }

            if (string.Equals(
                    normalizedBaseCurrency,
                    Currency,
                    StringComparison.Ordinal)
                && (exchangeRate != 1m
                    || validatedBaseAmounts != validatedOrderAmounts))
            {
                throw new DomainRuleViolationException(
                    "order_same_currency_snapshot_invalid",
                    "Đơn hàng dùng tiền tệ cơ sở phải có tỷ giá 1 và số tiền không đổi.");
            }

            BaseCurrency = normalizedBaseCurrency;
            ExchangeRate = exchangeRate;
            ExchangeRateCapturedAt = exchangeRateCapturedAt;
            BaseSubtotalAmount = validatedBaseAmounts.Subtotal;
            BaseDiscountAmount = validatedBaseAmounts.Discount;
            BaseShippingFee = validatedBaseAmounts.Shipping;
            BaseTaxAmount = validatedBaseAmounts.Tax;
            BaseTotalAmount = validatedBaseAmounts.Total;
            SubtotalAmount = validatedOrderAmounts.Subtotal;
            DiscountAmount = validatedOrderAmounts.Discount;
            ShippingFee = validatedOrderAmounts.Shipping;
            TaxAmount = validatedOrderAmounts.Tax;
            TotalAmount = validatedOrderAmounts.Total;
        }

        public StatusChange<OrderStatus> ChangeStatus(
            OrderStatus nextStatus,
            PaymentStatus? paymentStatus)
        {
            var previousStatus = Status;
            if (previousStatus == nextStatus)
                return new StatusChange<OrderStatus>(previousStatus, nextStatus, false);

            if (!previousStatus.CanTransitionTo(nextStatus))
            {
                throw new DomainRuleViolationException(
                    "order_status_transition_invalid",
                    $"Không thể chuyển đơn hàng từ trạng thái '{GetStatusLabel(previousStatus)}' sang '{GetStatusLabel(nextStatus)}'.");
            }

            if (nextStatus == OrderStatus.Cancelled
                && paymentStatus == PaymentStatus.Paid)
            {
                throw new DomainRuleViolationException(
                    "order_paid_cancellation_forbidden",
                    "Không thể hủy đơn hàng đã thanh toán trước khi hoàn tiền.");
            }

            if (nextStatus == OrderStatus.Cancelled
                && paymentStatus == PaymentStatus.Refunded)
            {
                throw new DomainRuleViolationException(
                    "order_refunded_cancellation_forbidden",
                    "Không thể hủy đơn hàng có giao dịch đã hoàn tiền.");
            }

            if (nextStatus == OrderStatus.Returned
                && paymentStatus is not (PaymentStatus.Paid or PaymentStatus.Refunded))
            {
                throw new DomainRuleViolationException(
                    "order_return_requires_collected_payment",
                    "Chỉ có thể ghi nhận hoàn hàng sau khi đơn hàng đã thu tiền.");
            }

            if ((nextStatus is OrderStatus.ReturnRequested
                or OrderStatus.ReturnApproved)
                && paymentStatus != PaymentStatus.Paid)
            {
                throw new DomainRuleViolationException(
                    "order_return_requires_paid_payment",
                    "Chỉ có thể xử lý trả hàng cho đơn đã thanh toán.");
            }

            if (nextStatus == OrderStatus.Refunded
                && paymentStatus != PaymentStatus.Refunded)
            {
                throw new DomainRuleViolationException(
                    "order_refunded_requires_refunded_payment",
                    "Chỉ có thể hoàn tất đơn hàng sau khi giao dịch đã được hoàn tiền.");
            }

            Status = nextStatus;
            return new StatusChange<OrderStatus>(previousStatus, nextStatus, true);
        }

        public void SetPendingExpiration(DateTime expiresAt)
        {
            if (Status != OrderStatus.Pending)
            {
                throw new DomainRuleViolationException(
                    "order_expiration_requires_pending",
                    "Chỉ đơn hàng đang chờ xác nhận mới có thể đặt thời hạn xử lý.");
            }

            if (expiresAt <= OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_expiration_invalid",
                    "Thời hạn xử lý đơn hàng phải sau thời điểm đặt hàng.");
            }

            ExpiresAt = expiresAt;
        }

        public StatusChange<OrderStatus> Cancel(
            DateTime occurredAt,
            PaymentStatus? paymentStatus,
            string reason,
            bool isExpiration = false)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200)
            {
                throw new DomainRuleViolationException(
                    "order_cancellation_reason_invalid",
                    "Lý do hủy phải có từ 1 đến 200 ký tự.");
            }

            if (occurredAt < OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_cancellation_time_invalid",
                    "Thời điểm hủy không được trước thời điểm đặt hàng.");
            }

            if (isExpiration && (!ExpiresAt.HasValue || occurredAt < ExpiresAt.Value))
            {
                throw new DomainRuleViolationException(
                    "order_not_expired",
                    "Đơn hàng chưa đến thời điểm hết hạn.");
            }

            var statusChange = ChangeStatus(OrderStatus.Cancelled, paymentStatus);
            if (!statusChange.Changed)
                return statusChange;

            CancelledAt = occurredAt;
            CancellationReason = reason.Trim();
            ExpiredAt = isExpiration ? occurredAt : null;
            return statusChange;
        }

        private static string GetStatusLabel(OrderStatus status)
            => status switch
            {
                OrderStatus.Pending => "Chờ xác nhận",
                OrderStatus.Confirmed => "Đã xác nhận",
                OrderStatus.Shipping => "Đang giao",
                OrderStatus.Delivered => "Đã giao",
                OrderStatus.Cancelled => "Đã hủy",
                OrderStatus.DeliveryFailed => "Giao thất bại",
                OrderStatus.Returned => "Đã nhận hàng hoàn",
                OrderStatus.ReturnRequested => "Đã yêu cầu trả hàng",
                OrderStatus.ReturnApproved => "Đã duyệt trả hàng",
                OrderStatus.Refunded => "Đã hoàn tiền",
                _ => status.ToString()
            };

        private static bool IsValidPhone(string phone)
        {
            var digitStart = phone.StartsWith("+84", StringComparison.Ordinal)
                ? 3
                : phone.StartsWith('0')
                    ? 1
                    : -1;
            if (digitStart < 0 || phone.Length - digitStart != 9)
                return false;

            return phone.AsSpan(digitStart).IndexOfAnyExceptInRange(
                '0',
                '9') < 0;
        }
    }
}
