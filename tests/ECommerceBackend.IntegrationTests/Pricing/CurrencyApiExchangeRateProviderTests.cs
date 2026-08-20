using System.Net;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Infrastructure.Pricing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class CurrencyApiExchangeRateProviderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetRate_UsesHeaderAuthenticationAndCachesPair()
    {
        var handler = new QueueHandler(SuccessResponse());
        var timeProvider = new MutableTimeProvider(Now);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(handler, cache, timeProvider);

        var first = await provider.GetRateAsync("vnd", "usd");
        var second = await provider.GetRateAsync("VND", "USD");

        Assert.Equal("VND", first.BaseCurrency);
        Assert.Equal("USD", first.QuoteCurrency);
        Assert.Equal(0.00003846m, first.Rate);
        Assert.Equal(
            new DateTime(2026, 8, 18, 11, 0, 0, DateTimeKind.Utc),
            first.CapturedAt);
        Assert.False(first.IsStale);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("test-api-key-123456", handler.ApiKey);
        Assert.Equal(
            "https://api.currencyapi.com/v3/latest?base_currency=VND&currencies=USD",
            handler.RequestUri);
    }

    [Fact]
    public async Task ProviderFailure_UsesBoundedStaleRate()
    {
        var handler = new QueueHandler(
            SuccessResponse(),
            (HttpStatusCode.InternalServerError, "{}"));
        var timeProvider = new MutableTimeProvider(Now);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(
            handler,
            cache,
            timeProvider,
            cacheMinutes: 1,
            maxStaleMinutes: 5);
        var fresh = await provider.GetRateAsync("VND", "EUR");
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var stale = await provider.GetRateAsync("VND", "EUR");

        Assert.Equal("EUR", fresh.QuoteCurrency);
        Assert.Equal(0.00003520m, fresh.Rate);
        Assert.True(stale.IsStale);
        Assert.Equal(fresh.Rate, stale.Rate);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ProviderFailure_AfterStaleWindowReturnsStableError()
    {
        var handler = new QueueHandler(
            SuccessResponse(),
            (HttpStatusCode.InternalServerError, "{}"));
        var timeProvider = new MutableTimeProvider(Now);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(
            handler,
            cache,
            timeProvider,
            cacheMinutes: 1,
            maxStaleMinutes: 5);
        _ = await provider.GetRateAsync("VND", "USD");
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            provider.GetRateAsync("VND", "USD"));

        Assert.Equal("exchange_rate_provider_error", exception.Code);
    }

    private static CurrencyApiExchangeRateProvider CreateProvider(
        HttpMessageHandler handler,
        IMemoryCache cache,
        TimeProvider timeProvider,
        int cacheMinutes = 60,
        int maxStaleMinutes = 1440)
        => new(
            new StaticHttpClientFactory(new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.currencyapi.com/")
            }),
            cache,
            Options.Create(new ExchangeRateOptions
            {
                Enabled = true,
                ApiKey = "test-api-key-123456",
                CacheMinutes = cacheMinutes,
                MaxStaleMinutes = maxStaleMinutes
            }),
            timeProvider,
            NullLogger<CurrencyApiExchangeRateProvider>.Instance);

    private static (HttpStatusCode Status, string Body) SuccessResponse()
        => (HttpStatusCode.OK,
            """
            {
              "meta": {
                "last_updated_at": "2026-08-18T11:00:00Z"
              },
              "data": {
                "USD": { "code": "USD", "value": 0.00003846 },
                "EUR": { "code": "EUR", "value": 0.00003520 }
              }
            }
            """);

    private sealed class QueueHandler(
        params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)>
            _responses = new(responses);

        public int CallCount { get; private set; }
        public string? ApiKey { get; private set; }
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ApiKey = request.Headers.TryGetValues("apikey", out var values)
                ? values.Single()
                : null;
            RequestUri = request.RequestUri?.AbsoluteUri;
            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(
                    response.Body,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
            => _utcNow = _utcNow.Add(duration);
    }
}
