using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using IndirimTakip.Infrastructure.Scraping.Huel;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// Pages are shaped like huel.com's on 2026-10-10: JSON-LD offers plus the product object in the
// React payload (self.__next_f.push), with the payload's "$undefined" for missing values.
public class HuelScraperTests
{
    private const string BlackEdition = """
        {"productId":"9ace5404","handle":"huel-black-edition","title":"Black Edition",
         "featuredImage":"https://huel.imgix.net/be.png","deliveryUnit":"bag","isMerchProduct":false,"isBundle":false,
         "variants":[
          {"name":"Variety Box (16 single-serve packs)","isOutOfStock":false,"pricing":{"subscriptionPrice":6000,"onetimePrice":7500},
           "servingQuantity":16,"deliveryUnit":"box","shopifyVariantGuid":"gid://shopify/ProductVariant/43822172504175","isComingSoon":false},
          {"name":"Vanilla","isOutOfStock":false,"pricing":{"subscriptionPrice":4500,"onetimePrice":6000},
           "servingQuantity":17,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/41890740306031","isComingSoon":false},
          {"name":"Chocolate","isOutOfStock":false,"pricing":{"subscriptionPrice":4500,"onetimePrice":6000},
           "servingQuantity":17,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/41890740371567","isComingSoon":false},
          {"name":"Chocolate Peanut Butter","isOutOfStock":true,"pricing":{"subscriptionPrice":4500,"onetimePrice":5500},
           "servingQuantity":17,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/53300000000001","isComingSoon":false},
          {"name":"Bestseller Trio (3 bags | 10 meals each)","isOutOfStock":true,"pricing":{"subscriptionPrice":9500,"onetimePrice":12665},
           "servingQuantity":30,"deliveryUnit":"box","shopifyVariantGuid":"gid://shopify/ProductVariant/42912640368751","isComingSoon":false}
         ]}
        """;
    private static readonly decimal[] BlackEditionPrices = [55.00m, 60.00m, 60.00m, 75.00m, 126.65m];

    private const string Essential = """
        {"productId":"e1","handle":"huel-essential","title":"Essential","featuredImage":"$undefined","deliveryUnit":"bag",
         "isMerchProduct":false,"isBundle":false,
         "variants":[
          {"name":"Chocolate","isOutOfStock":false,"pricing":{"subscriptionPrice":"$undefined","onetimePrice":4465},
           "servingQuantity":22.5,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/1001","isComingSoon":false},
          {"name":"Vanilla","isOutOfStock":false,"pricing":{"subscriptionPrice":"$undefined","onetimePrice":4465},
           "servingQuantity":22.5,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/1002","isComingSoon":false}
         ]}
        """;

    // The page's own handle written with a slash, and a bundle whose only variant has no name.
    private const string Bundle = """
        {"productId":"b1","handle":"/discovery-bundle-v2","title":"Discovery Bundle","deliveryUnit":"box",
         "isMerchProduct":false,"isBundle":true,
         "variants":[
          {"name":"","isOutOfStock":false,"pricing":{"subscriptionPrice":5600,"onetimePrice":7465},
           "servingQuantity":17,"deliveryUnit":"$undefined","shopifyVariantGuid":"gid://shopify/ProductVariant/44540740730991","isComingSoon":false}
         ]}
        """;

    private static string Page(string product, string currency, params decimal[] structuredPrices)
    {
        var offers = string.Join(",", structuredPrices.Select(p =>
            $$$"""{"@type":"Product","offers":{"@type":"Offer","price":"{{{p.ToString("0.00", CultureInfo.InvariantCulture)}}}","priceCurrency":"{{{currency}}}"}}"""));
        var jsonLd = $$$"""{"@context":"https://schema.org","@type":"ProductGroup","category":"Nutritionally Complete Powders","hasVariant":[{{{offers}}}]}""";
        var payload = $$$"""96:["$","div",null,{"children":["$","$La6",null,{"product":{{{product}}}}]}]""" + "\n";
        return $"""
            <html><head><script type="application/ld+json">{jsonLd}</script></head>
            <body><script>self.__next_f.push([1,{JsonSerializer.Serialize(payload)}])</script></body></html>
            """;
    }

    private sealed class Site(Dictionary<string, Func<HttpResponseMessage>> pages) : HttpMessageHandler
    {
        public List<string?> Cookies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Cookies.Add(request.Headers.TryGetValues("Cookie", out var c) ? string.Join("; ", c) : null);
            var handle = request.RequestUri!.AbsolutePath.Replace("/products/", "");
            return Task.FromResult(pages.TryGetValue(handle, out var page) ? page() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static Func<HttpResponseMessage> Html(string html) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static (HuelScraper Scraper, Site Site) Scraper(SiteMarket market, Dictionary<string, Func<HttpResponseMessage>> pages)
    {
        var baseUrl = market == SiteMarket.Uk ? "https://uk.huel.com" : "https://huel.com";
        var store = new ShopifyStore("Huel", baseUrl, Market: market == SiteMarket.Uk ? SiteMarket.Uk : null,
            OnlyHandles: new HashSet<string>(pages.Keys, StringComparer.OrdinalIgnoreCase));
        var site = new Site(pages);
        return (new HuelScraper(new HttpClient(site), store, NullLogger<HuelScraper>.Instance, TimeSpan.Zero), site);
    }

    // A variant naming its own delivery unit is another package (Black Edition's box and trio);
    // the flavors share the bag. Each package keeps its lowest variant id in the address, as
    // products.json rows did, and is priced by its cheapest flavor IN STOCK, in dollars.
    [Fact]
    public async Task Packages_become_rows_and_flavors_collapse()
    {
        var (scraper, _) = Scraper(SiteMarket.Us, new() { ["huel-black-edition"] = Html(Page(BlackEdition, "USD", BlackEditionPrices)) });

        var rows = (await scraper.ScrapeAsync()).OrderBy(r => r.Price).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal("https://huel.com/products/huel-black-edition?variant=41890740306031", rows[0].Url);
        Assert.Equal("Black Edition - 17 servings", rows[0].Name);
        Assert.Equal(60.00m, rows[0].Price);
        Assert.Equal(17, rows[0].ServingsPerPackage);
        Assert.True(rows[0].InStock);
        Assert.Equal("https://huel.com/products/huel-black-edition?variant=43822172504175", rows[1].Url);
        Assert.Equal(75.00m, rows[1].Price);
        Assert.Equal("https://huel.com/products/huel-black-edition?variant=42912640368751", rows[2].Url);
        Assert.Equal(126.65m, rows[2].Price);
        Assert.False(rows[2].InStock);
    }

    // One package in flavors keeps the plain address; "$undefined" in a number field is tolerated.
    [Fact]
    public async Task One_package_keeps_the_plain_address()
    {
        var (scraper, _) = Scraper(SiteMarket.Us, new() { ["huel-essential"] = Html(Page(Essential, "USD", 44.65m, 44.65m)) });

        var row = Assert.Single(await scraper.ScrapeAsync());

        Assert.Equal("https://huel.com/products/huel-essential", row.Url);
        Assert.Equal("Essential", row.Name);
        Assert.Equal(44.65m, row.Price);
    }

    // Measured 2026-10-10: the payload wrote "/glp1-companion-pack" and an empty variant name.
    [Fact]
    public async Task Bundle_keeps_its_variant_and_the_page_handle()
    {
        var (scraper, _) = Scraper(SiteMarket.Us, new() { ["discovery-bundle-v2"] = Html(Page(Bundle, "USD", 74.65m)) });

        var row = Assert.Single(await scraper.ScrapeAsync());

        Assert.Equal("https://huel.com/products/discovery-bundle-v2?variant=44540740730991", row.Url);
        Assert.Equal("Discovery Bundle", row.Name);
        Assert.Equal(74.65m, row.Price);
    }

    // The UK page for huel-daily-greens-ready-to-drink embeds "daily-greens-ready-to-drink":
    // the address is still the page's.
    [Fact]
    public async Task A_page_with_one_product_is_that_product()
    {
        var (scraper, _) = Scraper(SiteMarket.Us, new() { ["huel-essential-v2"] = Html(Page(Essential, "USD", 44.65m)) });

        var row = Assert.Single(await scraper.ScrapeAsync());

        Assert.Equal("https://huel.com/products/huel-essential-v2", row.Url);
    }

    // The request carries the edition's locale; without it huel.com sends Frankfurt to de.huel.com.
    [Theory]
    [InlineData("US", "huel_locale=en-US")]
    [InlineData("UK", "huel_locale=en-GB")]
    public async Task Request_names_the_edition_locale(string market, string cookie)
    {
        var siteMarket = market == "UK" ? SiteMarket.Uk : SiteMarket.Us;
        var (scraper, site) = Scraper(siteMarket, new() { ["huel-essential"] = Html(Page(Essential, siteMarket.Currency, 44.65m)) });

        await scraper.ScrapeAsync();

        Assert.Equal([cookie], site.Cookies);
    }

    // A page priced in another currency (the German store) must never be ingested.
    [Fact]
    public async Task Another_currency_stops_the_run()
    {
        var (scraper, _) = Scraper(SiteMarket.Us, new() { ["huel-essential"] = Html(Page(Essential, "EUR", 44.65m)) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());
    }

    // If the embedded price stops being one-time cents (a subscription or per-meal figure, another
    // unit), it no longer matches the page's own prices and the page is not read.
    [Theory]
    [InlineData(4465.00)]
    [InlineData(33.49)]
    public void Embedded_price_must_match_the_structured_data(double structured)
    {
        Assert.Null(HuelScraper.ReadPage(Page(Essential, "USD", (decimal)structured), "huel-essential", "USD"));
    }

    // Redirects are off in production: a 307 (to de.huel.com) is a failed page, not read; if
    // most pages fail the run fails instead of passing as a smaller catalog.
    [Fact]
    public async Task Redirected_pages_fail()
    {
        static HttpResponseMessage Redirect() => new(HttpStatusCode.TemporaryRedirect)
        {
            Headers = { Location = new Uri("https://de.huel.com/products/huel") },
        };

        var (one, _) = Scraper(SiteMarket.Us, new()
        {
            ["huel-essential"] = Html(Page(Essential, "USD", 44.65m)),
            ["huel"] = Redirect,
        });
        Assert.Equal(["https://huel.com/products/huel-essential"], (await one.ScrapeAsync()).Select(r => r.Url));

        var (all, _) = Scraper(SiteMarket.Us, new() { ["huel"] = Redirect, ["huel-bar"] = Redirect });
        await Assert.ThrowsAsync<InvalidOperationException>(() => all.ScrapeAsync());
    }

    [Fact]
    public void Each_edition_has_its_own_store()
    {
        Assert.Equal("https://huel.com", HuelScraper.ForMarket(SiteMarket.Us).BaseUrl);
        Assert.Equal("https://uk.huel.com", HuelScraper.ForMarket(SiteMarket.Uk).BaseUrl);
        Assert.Equal(SiteMarket.Uk, HuelScraper.ForMarket(SiteMarket.Uk).StoreMarket);
    }
}
