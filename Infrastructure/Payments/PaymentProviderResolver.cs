using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class PaymentProviderResolver : IPaymentProviderResolver
    {
        private readonly IReadOnlyDictionary<string, IPaymentProvider> _providersByCode;
        private readonly IReadOnlyDictionary<PaymentMethod, IPaymentProvider> _checkoutProviders;

        public PaymentProviderResolver(IEnumerable<IPaymentProvider> providers)
        {
            var providerList = providers.ToArray();
            var providersByCode = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);
            var checkoutProviders = new Dictionary<PaymentMethod, IPaymentProvider>();

            foreach (var provider in providerList)
            {
                var code = provider.Code?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                    throw new InvalidOperationException("Payment provider code cannot be empty.");

                if (!providersByCode.TryAdd(code, provider))
                    throw new InvalidOperationException($"Payment provider code '{code}' is registered more than once.");

                if (provider.CheckoutMethod is not { } method)
                    continue;

                if (!checkoutProviders.TryAdd(method, provider))
                {
                    throw new InvalidOperationException(
                        $"Payment method '{method}' has more than one checkout provider.");
                }
            }

            _providersByCode = providersByCode;
            _checkoutProviders = checkoutProviders;
        }

        public IPaymentProvider GetCheckoutProvider(PaymentMethod method)
            => _checkoutProviders.TryGetValue(method, out var provider)
                ? provider
                : throw new BusinessException("Phương thức thanh toán chưa được hỗ trợ.");

        public IPaymentProvider GetWebhookProvider(string providerCode)
        {
            var normalizedCode = providerCode.Trim();
            if (!_providersByCode.TryGetValue(normalizedCode, out var provider) || !provider.SupportsWebhooks)
                throw new NotFoundException("Không tìm thấy payment webhook provider đang hoạt động.");

            return provider;
        }

        public IReadOnlyList<PaymentCheckoutCapability> GetCheckoutCapabilities()
            => _checkoutProviders
                .OrderBy(pair => pair.Key)
                .Select(pair => new PaymentCheckoutCapability(
                    pair.Key,
                    pair.Value.Code,
                    pair.Value.SupportsWebhooks))
                .ToArray();
    }
}
