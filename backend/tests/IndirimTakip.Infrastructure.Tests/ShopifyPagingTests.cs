using System.Net;
using System.Text;
using System.Web;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// Shopify pages the whole catalog first and drops the products the requested
// market doesn't sell after: Optimum Nutrition /en-gb answered 49, then 23,
// then 0 products (2026-10-05). Stopping at the short first page had kept the
// 23 off the site, so only an empty page may end the crawl.
public class ShopifyPagingTests
{
    private static string Product(string handle) => $$"""
        {"title":"Whey Protein {{handle}}","handle":"{{handle}}","product_type":"Protein Powder","tags":[],
         "options":[{"name":"Title","position":1}],
         "variants":[{"id":1,"option1":"Default Title","price":"39.99","available":true}],"images":[]}
        """;

    private static string Page(params string[] handles) =>
        $$"""{"products":[{{string.Join(",", handles.Select(Product))}}]}""";

    /// <summary>Serves the given pages in order and an empty page after them.</summary>
    private sealed class PagedCatalog(Func<int, string> page) : HttpMessageHandler
    {
        public List<int> PagesRequested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            // The storefront check before the catalog: answer it in USD.
            if (!uri.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""Shopify.currency = {"active":"USD","rate":"1.0"};""", Encoding.UTF8, "text/html"),
                });

            var number = int.Parse(HttpUtility.ParseQueryString(uri.Query)["page"]!);
            PagesRequested.Add(number);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page(number), Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<(IReadOnlyList<string> Handles, List<int> Pages)> Crawl(Func<int, string> page)
    {
        var catalog = new PagedCatalog(page);
        var scraper = new ShopifyStoreScraper(
            new HttpClient(catalog), new ShopifyStore("Optimum Nutrition", "https://www.optimumnutrition.com/en-us"),
            NullLogger<ShopifyStoreScraper>.Instance, rateLimitRetryDelay: TimeSpan.Zero, pageDelay: TimeSpan.Zero);
        var items = await scraper.ScrapeAsync();
        return (items.Select(i => new Uri(i.Url).Segments[^1]).ToList(), catalog.PagesRequested);
    }

    [Fact]
    public async Task A_short_page_followed_by_more_does_not_end_the_crawl()
    {
        var (handles, pages) = await Crawl(n => n switch
        {
            1 => Page("a", "b"),
            2 => Page("c"),
            _ => Page(),
        });

        Assert.Equal(["a", "b", "c"], handles);
        Assert.Equal([1, 2, 3], pages);
    }

    [Fact]
    public async Task An_empty_first_page_ends_the_crawl_at_once()
    {
        var (handles, pages) = await Crawl(_ => Page());

        Assert.Empty(handles);
        Assert.Equal([1], pages);
    }

    // The page limit still bounds a store that never answers an empty page.
    [Fact]
    public async Task A_catalog_without_an_empty_page_stops_at_the_page_limit()
    {
        var (_, pages) = await Crawl(n => Page($"p{n}"));

        Assert.Equal(20, pages.Count);
    }
}
