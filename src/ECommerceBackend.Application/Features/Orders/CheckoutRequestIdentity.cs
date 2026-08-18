using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Application.Services
{
    internal static class CheckoutRequestIdentity
    {
        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BusinessException(
                    "Trường Idempotency-Key trong tiêu đề yêu cầu "
                    + "là bắt buộc khi đặt hàng.");
            }

            var normalized = value.Trim();
            if (normalized.Length > 100)
            {
                throw new BusinessException(
                    "Trường Idempotency-Key trong tiêu đề yêu cầu "
                    + "không được vượt quá 100 ký tự.");
            }

            return normalized;
        }

        public static string Hash(PlaceOrderRequest request)
        {
            var baseCanonical = string.Join(
                '\n',
                request.ShippingAddress.Trim(),
                NormalizeOptional(request.Note) ?? string.Empty,
                ((int)request.PaymentMethod).ToString());
            var normalizedPromotionCode =
                string.IsNullOrWhiteSpace(request.PromotionCode)
                    ? null
                    : DomainRuleGuard.AsBusiness(() =>
                        Promotion.NormalizeCode(
                            request.PromotionCode));
            var canonical =
                request.ShippingMethod == ShippingMethod.Standard
                && normalizedPromotionCode == null
                && !request.ExpectedTotalAmount.HasValue
                    ? baseCanonical
                    : string.Join(
                        '\n',
                        baseCanonical,
                        ((int)request.ShippingMethod).ToString(),
                        normalizedPromotionCode ?? string.Empty);
            if (request.ExpectedTotalAmount.HasValue)
            {
                canonical = string.Join(
                    '\n',
                    canonical,
                    request.ExpectedTotalAmount.Value.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture));
            }

            var normalizedCurrency = string.IsNullOrWhiteSpace(
                request.Currency)
                    ? CurrencyCatalog.BaseCurrency
                    : CurrencyCatalog.Normalize(request.Currency);
            if (!string.Equals(
                    normalizedCurrency,
                    CurrencyCatalog.BaseCurrency,
                    StringComparison.Ordinal))
            {
                canonical = string.Join(
                    '\n',
                    canonical,
                    normalizedCurrency);
            }

            var recipientName = NormalizeOptional(request.RecipientName);
            var recipientPhone = NormalizeOptional(request.RecipientPhone);
            if (recipientName != null || recipientPhone != null)
            {
                canonical = string.Join(
                    '\n',
                    canonical,
                    recipientName ?? string.Empty,
                    recipientPhone ?? string.Empty);
            }

            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        public static void EnsureSameRequest(
            Order order,
            string requestHash)
        {
            if (!string.Equals(
                order.IdempotencyRequestHash,
                requestHash,
                StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "Idempotency-Key đã được sử dụng cho "
                    + "một yêu cầu đặt hàng khác.");
            }
        }

        public static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
