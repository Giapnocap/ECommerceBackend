using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Tests.Support;

internal sealed class TestExchangeRateProvider : IExchangeRateProvider
{
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<(string Base, string Quote), decimal>
        _rates;

    public TestExchangeRateProvider(
        TimeProvider timeProvider,
        IReadOnlyDictionary<(string Base, string Quote), decimal>? rates = null)
    {
        _timeProvider = timeProvider;
        _rates = rates
            ?? new Dictionary<(string Base, string Quote), decimal>();
    }

    public Task<ExchangeRateQuote> GetRateAsync(
        string baseCurrency,
        string quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedBase = CurrencyCatalog.Normalize(baseCurrency);
        var normalizedQuote = CurrencyCatalog.Normalize(quoteCurrency);
        var rate = string.Equals(
            normalizedBase,
            normalizedQuote,
            StringComparison.Ordinal)
                ? 1m
                : _rates.TryGetValue(
                    (normalizedBase, normalizedQuote),
                    out var configuredRate)
                        ? configuredRate
                        : throw new BusinessException(
                            "exchange_rate_missing",
                            "Test exchange rate is not configured.");
        return Task.FromResult(new ExchangeRateQuote(
            normalizedBase,
            normalizedQuote,
            rate,
            _timeProvider.GetUtcNow().UtcDateTime,
            false));
    }
}
