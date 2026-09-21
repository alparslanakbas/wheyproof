using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// Huel: the public site geo-redirects our Frankfurt server to its German store,
// so the catalog is read from the Shopify backend while links point at huel.com.
public class ShopifyCatalogHostTests
{
    private const string Catalog = """
        {"products":[
          {"title":"Black Edition","handle":"black-edition","product_type":"Huel Powder","tags":[],
           "options":[{"name":"Title","position":1}],
           "variants":[{"id":1,"option1":"Default Title","price":"39.00","available":true}],"images":[]},
          {"title":"Huel Complete Nutrition Bar","handle":"hidden-bar","product_type":"Huel Bars","tags":["HUEL_HIDDEN"],
           "options":[{"name":"Title","position":1}],
           "variants":[{"id":2,"option1":"Default Title","price":"1.78","available":true}],"images":[]},
          {"title":"Huel Performance Cap","handle":"cap","product_type":"Merch","tags":[],
           "options":[{"name":"Title","position":1}],
           "variants":[{"id":3,"option1":"Default Title","price":"25.00","available":true}],"images":[]},
          {"title":"5x Complete Nutrition Bars","handle":"5x-complete-nutrition-bars","product_type":"Huel Bars","tags":[],
           "options":[{"name":"Flavor","position":1}],
           "variants":[{"id":4,"option1":"Chocolate","price":"3.55","available":true}],"images":[]}
        ]}
        """;

    private sealed class Handler(string shopCurrency) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requested.Add($"{uri.Host}{uri.AbsolutePath}");
            var body = uri.AbsolutePath switch
            {
                "/meta.json" => $$"""{"name":"Huel","currency":"{{shopCurrency}}"}""",
                "/products.json" => Catalog,
                _ => "",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static readonly ShopifyStore Huel = ShopifyStores.All.Single(s => s.BrandName == "Huel");

    [Fact]
    public async Task Catalog_is_read_from_the_backend_but_links_point_at_the_public_site()
    {
        var handler = new Handler("USD");
        var scraper = new ShopifyStoreScraper(new HttpClient(handler), Huel, NullLogger<ShopifyStoreScraper>.Instance);

        var products = await scraper.ScrapeAsync();

        Assert.Contains("huelamerica.myshopify.com/products.json", handler.Requested);
        Assert.DoesNotContain(handler.Requested, r => r.StartsWith("huel.com/products.json"));
        var item = Assert.Single(products);
        Assert.StartsWith("https://huel.com/products/black-edition", item.Url);
    }

    // The store's own markings (hidden tag, merch type) and a per-unit kit drop out;
    // only the real listing is left in the catalog above.
    [Fact]
    public async Task Hidden_merch_and_per_unit_kits_are_left_out()
    {
        var scraper = new ShopifyStoreScraper(new HttpClient(new Handler("USD")), Huel, NullLogger<ShopifyStoreScraper>.Instance);

        var products = await scraper.ScrapeAsync();

        Assert.Equal(["Black Edition"], products.Select(p => p.Name));
    }

    // A backend that reports another currency must stop the scrape: its prices
    // would otherwise be published as dollars.
    [Fact]
    public async Task Wrong_shop_currency_stops_the_scrape()
    {
        var scraper = new ShopifyStoreScraper(new HttpClient(new Handler("EUR")), Huel, NullLogger<ShopifyStoreScraper>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }
}
