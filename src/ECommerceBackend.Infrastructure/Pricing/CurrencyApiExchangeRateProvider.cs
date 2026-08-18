using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Pricing
{
    public sealed class CurrencyApiExchangeRateProvider
        : IExchangeRateProvider
    {
        public const string HttpClientName = "currency-api";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ExchangeRateOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<CurrencyApiExchangeRateProvider> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new(StringComparer.Ordinal);

        public CurrencyApiExchangeRateProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IOptions<ExchangeRateOptions> options,
            TimeProvider timeProvider,
            ILogger<CurrencyApiExchangeRateProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<ExchangeRateQuote> GetRateAsync(
            string baseCurrency,
            string quoteCurrency,
            CancellationToken cancellationToken = default)
        {
            var normalizedBase = NormalizeSupported(baseCurrency);
            var normalizedQuote = NormalizeSupported(quoteCurrency);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (string.Equals(
                    normalizedBase,
                    normalizedQuote,
                    StringComparison.Ordinal))
            {
                return new ExchangeRateQuote(
                    normalizedBase,
                    normalizedQuote,
                    1m,
                    now,
                    false);
            }

            if (!_options.Enabled)
            {
                throw new BusinessException(
                    "exchange_rate_provider_disabled",
                    "Dịch vụ tỷ giá chưa được bật.");
            }

            var cacheKey = $"fx:{normalizedBase}:{normalizedQuote}";
            if (TryGetFresh(cacheKey, now, out var cached))
                return cached;

            var gate = _locks.GetOrAdd(
                cacheKey,
                static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                now = _timeProvider.GetUtcNow().UtcDateTime;
                if (TryGetFresh(cacheKey, now, out cached))
                    return cached;

                try
                {
                    var fetched = await FetchAsync(
                        normalizedBase,
                        normalizedQuote,
                        cancellationToken);
                    var entry = new CachedExchangeRate(
                        fetched,
                        _timeProvider.GetUtcNow().UtcDateTime);
                    _cache.Set(
                        cacheKey,
                        entry,
                        TimeSpan.FromMinutes(_options.MaxStaleMinutes));
                    return fetched;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ApiException ex)
                    when (TryGetStale(cacheKey, now, out cached))
                {
                    _logger.LogWarning(
                        ex,
                        "Exchange rate provider failed; using stale cached rate for {BaseCurrency}/{QuoteCurrency}",
                        normalizedBase,
                        normalizedQuote);
                    return cached;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<ExchangeRateQuote> FetchAsync(
            string baseCurrency,
            string quoteCurrency,
            CancellationToken cancellationToken)
        {
            var path = "v3/latest?base_currency="
                + Uri.EscapeDataString(baseCurrency)
                + "&currencies="
                + Uri.EscapeDataString(quoteCurrency);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("apikey", _options.ApiKey);

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new ApiException(
                        503,
                        "exchange_rate_quota_exceeded",
                        "Dịch vụ tỷ giá đang giới hạn số lượng yêu cầu.");
                }

                if (!response.IsSuccessStatusCode)
                    throw ProviderError();

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                return ParseResponse(
                    document.RootElement,
                    baseCurrency,
                    quoteCurrency);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new ApiException(
                    504,
                    "exchange_rate_timeout",
                    "Dịch vụ tỷ giá phản hồi quá thời gian cho phép.");
            }
            catch (HttpRequestException ex)
            {
                throw ProviderError(ex);
            }
            catch (JsonException ex)
            {
                throw ProviderError(ex);
            }
        }

        private ExchangeRateQuote ParseResponse(
            JsonElement root,
            string baseCurrency,
            string quoteCurrency)
        {
            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty(quoteCurrency, out var quote)
                || !quote.TryGetProperty("code", out var code)
                || !string.Equals(
                    code.GetString(),
                    quoteCurrency,
                    StringComparison.Ordinal)
                || !quote.TryGetProperty("value", out var value)
                || !value.TryGetDecimal(out var rate)
                || rate <= 0)
            {
                throw ProviderError();
            }

            if (!root.TryGetProperty("meta", out var meta)
                || !meta.TryGetProperty("last_updated_at", out var updatedAt)
                || !DateTimeOffset.TryParse(
                    updatedAt.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal
                        | DateTimeStyles.AdjustToUniversal,
                    out var capturedAt))
            {
                throw ProviderError();
            }

            var now = _timeProvider.GetUtcNow();
            if (capturedAt > now.AddMinutes(5))
                throw ProviderError();

            var normalizedRate = decimal.Round(
                rate,
                10,
                MidpointRounding.ToEven);
            if (normalizedRate <= 0)
                throw ProviderError();

            return new ExchangeRateQuote(
                baseCurrency,
                quoteCurrency,
                normalizedRate,
                capturedAt.UtcDateTime,
                false);
        }

        private bool TryGetFresh(
            string cacheKey,
            DateTime now,
            out ExchangeRateQuote quote)
        {
            if (_cache.TryGetValue<CachedExchangeRate>(
                    cacheKey,
                    out var cached)
                && cached != null
                && now - cached.RetrievedAt
                    <= TimeSpan.FromMinutes(_options.CacheMinutes))
            {
                quote = cached.Quote with { IsStale = false };
                return true;
            }

            quote = default!;
            return false;
        }

        private bool TryGetStale(
            string cacheKey,
            DateTime now,
            out ExchangeRateQuote quote)
        {
            if (_cache.TryGetValue<CachedExchangeRate>(
                    cacheKey,
                    out var cached)
                && cached != null
                && now - cached.RetrievedAt
                    <= TimeSpan.FromMinutes(_options.MaxStaleMinutes))
            {
                quote = cached.Quote with { IsStale = true };
                return true;
            }

            quote = default!;
            return false;
        }

        private static string NormalizeSupported(string currency)
        {
            if (!CurrencyCatalog.IsSupported(currency))
            {
                throw new BusinessException(
                    "currency_not_supported",
                    "Tiền tệ yêu cầu chưa được hỗ trợ.");
            }

            return CurrencyCatalog.Normalize(currency);
        }

        private static ApiException ProviderError(
            Exception? innerException = null)
            => new(
                502,
                "exchange_rate_provider_error",
                "Không thể lấy tỷ giá tại thời điểm này.",
                innerException: innerException);

        private sealed record CachedExchangeRate(
            ExchangeRateQuote Quote,
            DateTime RetrievedAt);
    }
}
