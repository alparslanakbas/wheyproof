using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// Kaged answered 429 on its FIRST catalog page in two of three cycles on
// 2026-09-19 and 200 to a single request right after. Giving up at once left
// the store with no prices for the cycle; one delayed retry covers that.
public class ShopifyRateLimitRetryTests
{
    private const string OneProductPage = """
        {"products":[{"title":"Whey Protein","handle":"whey","product_type":"Protein Powder","tags":[],
        "options":[{"name":"Title","position":1}],
        "variants":[{"id":1,"option1":"Default Title","price":"39.99","available":true}],"images":[]}]}
        """;

    private sealed class ScriptedHandler(params HttpStatusCode[] catalogAnswers) : HttpMessageHandler
    {
        private int next;
        public int CatalogRequests { get; private set; }
        public TimeSpan? RetryAfter { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The storefront check before the catalog: answer it in USD.
            if (!request.RequestUri!.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""Shopify.currency = {"active":"USD","rate":"1.0"};""", Encoding.UTF8, "text/html"),
                });

            CatalogRequests++;
            var status = next < catalogAnswers.Length ? catalogAnswers[next++] : HttpStatusCode.OK;
            // One product on the first page, then the empty page that ends the catalog.
            var body = request.RequestUri.Query.Contains("&page=1&") ? OneProductPage : """{"products":[]}""";
            if (status == HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });

            var refused = new HttpResponseMessage(status);
            if (RetryAfter is { } wait)
                refused.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            return Task.FromResult(refused);
        }
    }

    private static (ShopifyStoreScraper Scraper, ScriptedHandler Handler) Build(params HttpStatusCode[] answers) =>
        Build(null, answers);

    private static (ShopifyStoreScraper Scraper, ScriptedHandler Handler) Build(TimeSpan? retryAfter, params HttpStatusCode[] answers)
    {
        var handler = new ScriptedHandler(answers) { RetryAfter = retryAfter };
        var scraper = new ShopifyStoreScraper(
            new HttpClient(handler), new ShopifyStore("Kaged", "https://www.kaged.com"),
            NullLogger<ShopifyStoreScraper>.Instance, rateLimitRetryDelay: TimeSpan.Zero, pageDelay: TimeSpan.Zero);
        return (scraper, handler);
    }

    [Fact]
    public async Task A_single_429_is_retried_and_the_page_is_kept()
    {
        var (scraper, handler) = Build(HttpStatusCode.TooManyRequests);

        var items = await scraper.ScrapeAsync();

        Assert.Single(items);
        // Page 1 twice (429, then the retry), then the empty page 2 that ends the crawl.
        Assert.Equal(3, handler.CatalogRequests);
    }

    // Only once: a store that keeps refusing us isn't pressed harder, and the
    // cycle moves on with nothing lost but this store's page.
    [Fact]
    public async Task A_second_429_gives_up_without_failing_the_store()
    {
        var (scraper, handler) = Build(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests);

        var items = await scraper.ScrapeAsync();

        Assert.Empty(items);
        Assert.Equal(2, handler.CatalogRequests);
    }

    // Shopify's review (2026-11-02): wait for the time in Retry-After, not a fixed delay.
    [Fact]
    public async Task The_retry_waits_for_the_Retry_After_header()
    {
        var (scraper, handler) = Build(TimeSpan.Zero, HttpStatusCode.TooManyRequests);

        var items = await scraper.ScrapeAsync();

        Assert.Single(items);
        Assert.Equal(3, handler.CatalogRequests);
    }

    // A store asking for more than the longest wait is left for the next cycle, not pressed.
    [Fact]
    public async Task A_Retry_After_beyond_the_longest_wait_is_not_retried()
    {
        var (scraper, handler) = Build(RetryAfter.MaxWait + TimeSpan.FromSeconds(1), HttpStatusCode.TooManyRequests);

        var items = await scraper.ScrapeAsync();

        Assert.Empty(items);
        Assert.Equal(1, handler.CatalogRequests);
    }

    [Fact]
    public void Retry_After_reads_seconds_and_dates_and_falls_back_without_one()
    {
        var now = new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        var fallback = TimeSpan.FromMinutes(1);
        var seconds = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        seconds.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var date = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        date.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(30));
        var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(-30));

        Assert.Equal(TimeSpan.FromSeconds(7), RetryAfter.Of(seconds, fallback, now));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfter.Of(date, fallback, now));
        Assert.Equal(TimeSpan.Zero, RetryAfter.Of(past, fallback, now));
        Assert.Equal(fallback, RetryAfter.Of(new HttpResponseMessage(HttpStatusCode.TooManyRequests), fallback, now));
    }
}
