namespace ECommerceBackend.Application.Interfaces
{
    public sealed record ExchangeRateQuote(
        string BaseCurrency,
        string QuoteCurrency,
        decimal Rate,
        DateTime CapturedAt,
        bool IsStale);

    public interface IExchangeRateProvider
    {
        Task<ExchangeRateQuote> GetRateAsync(
            string baseCurrency,
            string quoteCurrency,
            CancellationToken cancellationToken = default);
    }
}
