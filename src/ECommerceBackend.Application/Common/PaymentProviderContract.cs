using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Common
{
    public static class PaymentProviderContract
    {
        public const int MaxProviderCodeLength = 100;
        public const int MaxProviderTransactionIdLength = 200;

        public static string NormalizeCode(string? code)
        {
            var normalized = code?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > MaxProviderCodeLength
                || normalized.Any(character => !IsCodeCharacter(character)))
            {
                throw new InvalidOperationException(
                    "Payment provider code must contain only ASCII letters, digits, '.', '-' or '_'.");
            }

            return normalized;
        }

        public static PaymentInitializationResult NormalizeInitialization(
            IPaymentProvider provider,
            PaymentInitializationResult result)
        {
            var providerCode = NormalizeCode(provider.Code);
            var resultProvider = NormalizeCode(result.Provider);
            if (!string.Equals(providerCode, resultProvider, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Payment provider '{providerCode}' returned a different provider code.");
            }

            if (!Enum.IsDefined(result.Status)
                || !PaymentStatusTransitions.Initial.CanTransitionTo(result.Status))
            {
                throw new InvalidOperationException(
                    $"Payment provider '{providerCode}' returned invalid initial status '{result.Status}'.");
            }

            var transactionId = string.IsNullOrWhiteSpace(result.ProviderTransactionId)
                ? null
                : result.ProviderTransactionId.Trim();
            if (transactionId?.Length > MaxProviderTransactionIdLength)
            {
                throw new InvalidOperationException(
                    $"Payment provider '{providerCode}' returned a transaction ID that is too long.");
            }

            if (provider.SupportsWebhooks && transactionId is null)
            {
                throw new InvalidOperationException(
                    $"Checkout provider '{providerCode}' must return a transaction ID when webhooks are enabled.");
            }

            return result with
            {
                Provider = providerCode,
                ProviderTransactionId = transactionId
            };
        }

        private static bool IsCodeCharacter(char character)
            => character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.' or '-' or '_';
    }
}
