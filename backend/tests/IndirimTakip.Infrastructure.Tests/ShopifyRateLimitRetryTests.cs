using System.Net;
using System.Text;
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
            return Task.FromResult(status == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OneProductPage, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(status));
        }
    }

    private static (ShopifyStoreScraper Scraper, ScriptedHandler Handler) Build(params HttpStatusCode[] answers)
    {
        var handler = new ScriptedHandler(answers);
        var scraper = new ShopifyStoreScraper(
            new HttpClient(handler), new ShopifyStore("Kaged", "https://www.kaged.com"),
            NullLogger<ShopifyStoreScraper>.Instance, rateLimitRetryDelay: TimeSpan.Zero);
        return (scraper, handler);
    }

    [Fact]
    public async Task A_single_429_is_retried_and_the_page_is_kept()
    {
        var (scraper, handler) = Build(HttpStatusCode.TooManyRequests);

        var items = await scraper.ScrapeAsync();

        Assert.Single(items);
        Assert.Equal(2, handler.CatalogRequests);
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
}
