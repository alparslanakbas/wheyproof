using IndirimTakip.Infrastructure;
using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

public class ShopifyMarketQueryTests
{
    private sealed class Handler(string currency) : HttpMessageHandler
    {
        public List<string> CatalogQueries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/products.json")
                CatalogQueries.Add(uri.Query);
            // The storefront states its currency; the empty catalog ends the crawl.
            var body = uri.AbsolutePath == "/products.json"
                ? """{"products":[]}"""
                : $$"""<script>Shopify.currency = {"active":"{{currency}}","rate":"1.0"};</script>""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            });
        }
    }

    // Shopify prices and publishes a catalog per country market and, when the
    // request names none, goes by our server's location (Frankfurt): LOUCO then
    // priced its one-off purchases without UK VAT, and Gorilla Mind left out 19
    // US-only products, Gorilla Mode among them (measured 2026-09-25).
    [Theory]
    [InlineData("US", "Kaged", "currency=USD&country=US")]
    [InlineData("UK", "LOUCO", "currency=GBP&country=GB")]
    public async Task Catalog_request_names_the_market_country(string market, string brand, string expected)
    {
        var siteMarket = market == "UK" ? SiteMarket.Uk : SiteMarket.Us;
        var store = ShopifyStores.All.Single(s => s.BrandName == brand && s.StoreMarket == siteMarket);
        var handler = new Handler(siteMarket.Currency);
        var scraper = new ShopifyStoreScraper(new HttpClient(handler), store, NullLogger<ShopifyStoreScraper>.Instance, pageDelay: TimeSpan.Zero);

        await scraper.ScrapeAsync();

        Assert.NotEmpty(handler.CatalogQueries);
        Assert.All(handler.CatalogQueries, q => Assert.Contains(expected, q));
    }
}
