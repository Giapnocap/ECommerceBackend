using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class Promotion
    {
        public Guid Id { get; set; }
        public string Code { get; private set; } = string.Empty;
        public string NormalizedCode { get; private set; } = string.Empty;
        public PromotionType Type { get; private set; }
        public decimal Value { get; private set; }
        public decimal MinimumSubtotal { get; private set; }
        public decimal? MaximumDiscountAmount { get; private set; }
        public DateTime StartsAt { get; private set; }
        public DateTime EndsAt { get; private set; }
        public int UsageLimit { get; private set; }
        public int UsageLimitPerCustomer { get; private set; }
        public int UsedCount { get; private set; }
        public bool IsActive { get; private set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<PromotionRedemption> Redemptions { get; set; } =
            new List<PromotionRedemption>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();

        public static Promotion Create(
            Guid id,
            string code,
            PromotionType type,
            decimal value,
            decimal minimumSubtotal,
            decimal? maximumDiscountAmount,
            DateTime startsAt,
            DateTime endsAt,
            int usageLimit,
            int usageLimitPerCustomer,
            DateTime occurredAt)
        {
            var promotion = new Promotion
            {
                Id = id,
                CreatedAt = occurredAt,
                IsActive = true
            };
            promotion.Configure(
                code,
                type,
                value,
                minimumSubtotal,
                maximumDiscountAmount,
                startsAt,
                endsAt,
                usageLimit,
                usageLimitPerCustomer,
                occurredAt,
                isCreation: true);
            return promotion;
        }

        public void Update(
            PromotionType type,
            decimal value,
            decimal minimumSubtotal,
            decimal? maximumDiscountAmount,
            DateTime startsAt,
            DateTime endsAt,
            int usageLimit,
            int usageLimitPerCustomer,
            bool isActive,
            DateTime occurredAt)
        {
            if (usageLimit < UsedCount)
            {
                throw new DomainRuleViolationException(
                    "promotion_usage_limit_below_used_count",
                    "Giới hạn sử dụng không được nhỏ hơn số lượt đã dùng.");
            }

            Configure(
                Code,
                type,
                value,
                minimumSubtotal,
                maximumDiscountAmount,
                startsAt,
                endsAt,
                usageLimit,
                usageLimitPerCustomer,
                occurredAt,
                isCreation: false);

            IsActive = isActive;
            UpdatedAt = occurredAt;
        }

        public decimal CalculateDiscount(
            decimal subtotal,
            DateTime occurredAt,
            int customerUsageCount)
        {
            EnsureEligible(subtotal, occurredAt, customerUsageCount);

            decimal discount;
            try
            {
                discount = Type == PromotionType.FixedAmount
                    ? Value
                    : subtotal * Value / 100m;
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "promotion_discount_exceeded",
                    "Khoản giảm giá vượt quá giới hạn cho phép.");
            }

            discount = decimal.Round(
                discount,
                2,
                MidpointRounding.AwayFromZero);
            if (MaximumDiscountAmount.HasValue)
                discount = Math.Min(discount, MaximumDiscountAmount.Value);

            discount = Math.Min(discount, subtotal);
            if (discount <= 0)
            {
                throw new DomainRuleViolationException(
                    "promotion_discount_too_small",
                    "Giá trị giảm sau khi làm tròn phải lớn hơn 0.");
            }

            return discount;
        }

        public void Redeem(
            decimal subtotal,
            DateTime occurredAt,
            int customerUsageCount)
        {
            EnsureEligible(subtotal, occurredAt, customerUsageCount);
            UsedCount = checked(UsedCount + 1);
            UpdatedAt = occurredAt;
        }

        public bool Deactivate(DateTime occurredAt)
        {
            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "promotion_update_time_invalid",
                    "Thời điểm cập nhật promotion không hợp lệ.");
            }

            if (!IsActive)
                return false;

            IsActive = false;
            UpdatedAt = occurredAt;
            return true;
        }

        private void EnsureEligible(
            decimal subtotal,
            DateTime occurredAt,
            int customerUsageCount)
        {
            if (customerUsageCount < 0)
            {
                throw new DomainRuleViolationException(
                    "promotion_customer_usage_invalid",
                    "Số lượt sử dụng của khách hàng không hợp lệ.");
            }

            if (!IsActive)
            {
                throw new DomainRuleViolationException(
                    "promotion_inactive",
                    "Mã khuyến mãi không còn hoạt động.");
            }

            if (occurredAt < StartsAt)
            {
                throw new DomainRuleViolationException(
                    "promotion_not_started",
                    "Mã khuyến mãi chưa đến thời gian sử dụng.");
            }

            if (occurredAt >= EndsAt)
            {
                throw new DomainRuleViolationException(
                    "promotion_expired",
                    "Mã khuyến mãi đã hết hạn.");
            }

            if (subtotal < MinimumSubtotal)
            {
                throw new DomainRuleViolationException(
                    "promotion_minimum_subtotal_not_met",
                    $"Đơn hàng phải có tạm tính tối thiểu {MinimumSubtotal:N0} để sử dụng mã này.");
            }

            if (UsedCount >= UsageLimit)
            {
                throw new DomainRuleViolationException(
                    "promotion_usage_limit_reached",
                    "Mã khuyến mãi đã hết lượt sử dụng.");
            }

            if (customerUsageCount >= UsageLimitPerCustomer)
            {
                throw new DomainRuleViolationException(
                    "promotion_customer_limit_reached",
                    "Bạn đã sử dụng hết số lượt cho phép của mã khuyến mãi này.");
            }
        }

        private void Configure(
            string code,
            PromotionType type,
            decimal value,
            decimal minimumSubtotal,
            decimal? maximumDiscountAmount,
            DateTime startsAt,
            DateTime endsAt,
            int usageLimit,
            int usageLimitPerCustomer,
            DateTime occurredAt,
            bool isCreation)
        {
            var normalizedCode = NormalizeCode(code);
            if (!Enum.IsDefined(type))
            {
                throw new DomainRuleViolationException(
                    "promotion_type_invalid",
                    "Loại khuyến mãi không hợp lệ.");
            }

            OrderPricingPolicy.EnsureMoneyValue(
                value,
                "promotion_value_invalid",
                "Giá trị khuyến mãi");
            OrderPricingPolicy.EnsureMoneyValue(
                minimumSubtotal,
                "promotion_minimum_subtotal_invalid",
                "Tạm tính tối thiểu");
            if (maximumDiscountAmount.HasValue)
            {
                OrderPricingPolicy.EnsureMoneyValue(
                    maximumDiscountAmount.Value,
                    "promotion_maximum_discount_invalid",
                    "Mức giảm tối đa");
            }

            if (value <= 0
                || type == PromotionType.Percentage && value > 100)
            {
                throw new DomainRuleViolationException(
                    "promotion_value_invalid",
                    "Giá trị khuyến mãi phải lớn hơn 0; phần trăm không được vượt quá 100.");
            }

            if (type == PromotionType.FixedAmount
                && maximumDiscountAmount.HasValue)
            {
                throw new DomainRuleViolationException(
                    "promotion_maximum_discount_invalid",
                    "Khuyến mãi số tiền cố định không sử dụng mức giảm tối đa.");
            }

            if (maximumDiscountAmount is <= 0)
            {
                throw new DomainRuleViolationException(
                    "promotion_maximum_discount_invalid",
                    "Mức giảm tối đa phải lớn hơn 0.");
            }

            if (startsAt >= endsAt)
            {
                throw new DomainRuleViolationException(
                    "promotion_period_invalid",
                    "Thời gian kết thúc phải sau thời gian bắt đầu.");
            }

            if (startsAt.Kind != DateTimeKind.Utc
                || endsAt.Kind != DateTimeKind.Utc)
            {
                throw new DomainRuleViolationException(
                    "promotion_period_timezone_invalid",
                    "Thời gian khuyến mãi phải sử dụng UTC.");
            }

            if (usageLimit <= 0 || usageLimitPerCustomer <= 0)
            {
                throw new DomainRuleViolationException(
                    "promotion_usage_limit_invalid",
                    "Giới hạn sử dụng phải lớn hơn 0.");
            }

            if (occurredAt < CreatedAt && !isCreation)
            {
                throw new DomainRuleViolationException(
                    "promotion_update_time_invalid",
                    "Thời điểm cập nhật promotion không hợp lệ.");
            }

            Code = code.Trim().ToUpperInvariant();
            NormalizedCode = normalizedCode;
            Type = type;
            Value = value;
            MinimumSubtotal = minimumSubtotal;
            MaximumDiscountAmount = maximumDiscountAmount;
            StartsAt = startsAt;
            EndsAt = endsAt;
            UsageLimit = usageLimit;
            UsageLimitPerCustomer = usageLimitPerCustomer;
        }

        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new DomainRuleViolationException(
                    "promotion_code_invalid",
                    "Mã khuyến mãi không được để trống.");
            }

            var normalized = code.Trim().ToUpperInvariant();
            if (normalized.Length is < 3 or > 32
                || !normalized.All(character =>
                    character is >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '-' or '_'))
            {
                throw new DomainRuleViolationException(
                    "promotion_code_invalid",
                    "Mã khuyến mãi phải có 3-32 ký tự gồm chữ cái, số, dấu gạch ngang hoặc gạch dưới.");
            }

            return normalized;
        }
    }
}
