using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping.Myprotein;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Myprotein product pages embed the storefront's product object as
/// <c>const masterData = {...}</c>. The variants below follow the real Impact Whey
/// page as measured on 2026-10-03 (SKU, amount and price shapes taken from it).
/// </summary>
public class MyproteinScraperTests
{
    private const string ProductUrl = "https://www.myprotein.com/p/sports-nutrition/impact-whey-protein-powder/10530943/";

    private static string Variant(long sku, string flavour, string amount, string price, bool inStock, string rrp = "34.99") => $$$"""
        {"title":"Impact Whey Protein Powder - {{{amount}}} - {{{flavour}}}","sku":{{{sku}}},"barcode":"5056307353184","inStock":{{{(inStock ? "true" : "false")}}},
         "choices":[{"optionKey":"Flavour","key":"{{{flavour}}}","colour":null,"title":"{{{flavour}}}"},{"optionKey":"Amount","key":"{{{amount}}}","colour":null,"title":"{{{amount}}}"}],
         "price":{"price":{"currency":"GBP","amount":"{{{price}}}","displayValue":"£{{{price}}}"},"rrp":{"currency":"GBP","amount":"{{{rrp}}}","displayValue":"£{{{rrp}}}"}},
         "images":[{"original":"https://static.thcdn.com/productimg/original/{{{sku}}}.jpg","altText":null}]}
        """;

    private static string Page(string country, params string[] variants) => $$$"""
        <html><script>(function(){const session = {"currency":"GBP","shipping-destination":"{{{country}}}","locale":"en_GB","currencySymbol":"£"}; const masterData = {"pageTitle":"Impact Whey Protein Powder","masterSku":10530943,"userCountry":"{{{country}}}","defaultImages":[],"variants":[{{{string.Join(",", variants)}}}]}; const later = [1];})()</script></html>
        """;

    // One record per amount. The price is the cheapest IN-STOCK flavour (Dark
    // Chocolate at 32.99 is sold out); the address keeps the group's lowest SKU,
    // which is that sold-out flavour, so it doesn't move when stock changes.
    [Fact]
    public void Each_amount_is_one_record_priced_at_its_cheapest_in_stock_flavour()
    {
        var html = Page("GB",
            Variant(12270193, "Dark Chocolate", "1KG - 33servings", "32.99", inStock: false),
            Variant(12270199, "Vanilla", "1KG - 33servings", "34.49", inStock: true),
            Variant(12270210, "Chocolate Smooth", "1KG - 33servings", "36.99", inStock: true),
            Variant(12270300, "Dark Chocolate", "2.5KG - 83servings", "78.49", inStock: true, rrp: "89.99"));

        var records = MyproteinScraper.ToScrapedProducts(html, ProductUrl);

        Assert.Equal(2, records.Count);
        var kilo = Assert.Single(records, r => r.Name == "Impact Whey Protein Powder - 1KG - 33servings");
        Assert.Equal(34.49m, kilo.Price);
        Assert.Equal(34.99m, kilo.StoreOldPrice);
        Assert.Equal(ProductUrl + "?variation=12270193", kilo.Url);
        Assert.True(kilo.InStock);
        var big = Assert.Single(records, r => r.Name == "Impact Whey Protein Powder - 2.5KG - 83servings");
        Assert.Equal(78.49m, big.Price);
        Assert.Equal(89.99m, big.StoreOldPrice);
    }

    [Fact]
    public void An_amount_sold_out_in_every_flavour_stays_listed_as_out_of_stock()
    {
        var html = Page("GB",
            Variant(12270193, "Dark Chocolate", "1KG - 33servings", "32.99", inStock: false),
            Variant(12270199, "Vanilla", "1KG - 33servings", "34.49", inStock: false));

        var record = Assert.Single(MyproteinScraper.ToScrapedProducts(html, ProductUrl));

        Assert.False(record.InStock);
        Assert.Equal(32.99m, record.Price);
    }

    // A twin pack picks two flavours ("Flavour 1", "Flavour 2"); both are flavours,
    // not sizes, so the box is ONE record. The 2026-10-03 dry run made 36 records
    // out of one Layered Protein Bar box before this.
    [Fact]
    public void Two_flavour_options_are_both_flavours_not_sizes()
    {
        static string Box(long sku, string first, string second, string price) => $$$"""
            {"sku":{{{sku}}},"inStock":true,
             "choices":[{"optionKey":"Flavour 1","key":"{{{first}}}"},{"optionKey":"Flavour 2","key":"{{{second}}}"}],
             "price":{"price":{"currency":"GBP","amount":"{{{price}}}"}},"images":[]}
            """;
        var html = Page("GB",
            Box(16114270, "Chocolate Peanut Pretzel", "Cookie Crumble", "36.99"),
            Box(16114271, "Strawberry", "Cookie Crumble", "36.99"),
            Box(16114272, "Strawberry", "Strawberry", "34.99"));

        var record = Assert.Single(MyproteinScraper.ToScrapedProducts(html, ProductUrl));

        Assert.Equal("Impact Whey Protein Powder", record.Name);
        Assert.Equal(34.99m, record.Price);
        Assert.Equal(ProductUrl + "?variation=16114270", record.Url);
    }

    // Another brand's product sold by Myprotein is filed under that brand, with
    // Myprotein as the seller; Myprotein's own products carry neither.
    [Fact]
    public void Other_brands_are_filed_under_their_own_name_with_myprotein_as_seller()
    {
        var own = Page("GB", Variant(12270193, "Dark Chocolate", "1KG - 33servings", "32.99", inStock: true));
        var other = own.Replace("\"masterSku\":10530943", "\"masterSku\":10530943,\"brand\":\"Aduna\"");

        var mine = Assert.Single(MyproteinScraper.ToScrapedProducts(own, ProductUrl));
        var theirs = Assert.Single(MyproteinScraper.ToScrapedProducts(other, ProductUrl));

        Assert.Null(mine.BrandName);
        Assert.Null(mine.Seller);
        Assert.Equal("Aduna", theirs.BrandName);
        Assert.Equal("myprotein.com", theirs.Seller);
    }

    // Prices from another market must never reach the UK site: the run stops.
    [Fact]
    public void A_page_served_for_another_market_stops_the_run()
    {
        var html = Page("DE", Variant(12270193, "Dark Chocolate", "1KG - 33servings", "32.99", inStock: true));

        Assert.Throws<InvalidOperationException>(() => MyproteinScraper.ToScrapedProducts(html, ProductUrl));
    }

    [Fact]
    public void A_page_without_product_data_is_invalid()
    {
        Assert.Throws<InvalidDataException>(
            () => MyproteinScraper.ToScrapedProducts("<html><p>Access denied</p></html>", ProductUrl));
    }

    // A listing page that only repeats products already seen in THIS category ends
    // its paging; products found again under another category are fetched once.
    [Fact]
    public async Task Listings_are_paged_per_category_and_each_product_is_fetched_once()
    {
        const string a = "/p/sports-nutrition/impact-whey-protein-powder/10530943/";
        const string b = "/p/sports-nutrition/creatine-monohydrate-powder/10530050/";
        var pages = new Dictionary<string, string>
        {
            ["/c/nutrition/protein/"] = $"<a href=\"{a}\">x</a><a href=\"{a}\">x</a>",
            ["/c/nutrition/protein/?pageNumber=2"] = $"<a href=\"{b}\">x</a><a href=\"{a}\">x</a>",
            ["/c/nutrition/protein/?pageNumber=3"] = $"<a href=\"{b}\">x</a>",
            ["/c/nutrition/creatine/"] = $"<a href=\"{b}\">x</a>",
            [a] = Page("GB", Variant(12270193, "Dark Chocolate", "1KG - 33servings", "32.99", inStock: true)),
            [b] = Page("GB", Variant(10530054, "Unflavoured", "500g - 147servings", "19.99", inStock: true)),
        };
        var handler = new RoutingHandler(pages);
        var scraper = new MyproteinScraper(new HttpClient(handler), NullLogger<MyproteinScraper>.Instance, TimeSpan.Zero);

        var records = await scraper.ScrapeAsync();

        Assert.Equal(2, records.Count);
        Assert.Equal(1, handler.Requests.Count(r => r == a));
        Assert.Equal(1, handler.Requests.Count(r => r == b));
        Assert.DoesNotContain("/c/nutrition/protein/?pageNumber=4", handler.Requests);
    }

    private const string IsolateUrl = "https://www.myprotein.com/p/sports-nutrition/impact-whey-isolate-powder/10530911/";

    // The page as served for one variant: masterData names it, the panel shows its table first and
    // another flavour's further down.
    private static string DetailPage(string country, long activeSku) => $$$"""
        <html><body>{{{UkNutritionTableTests.MyproteinCaptainAmerica}}}<ul><li>{{{UkNutritionTableTests.MyproteinVanilla}}}</li></ul>
        <script>const masterData = {"pageTitle":"Impact Whey Isolate Powder","masterSku":10530911,"userCountry":"{{{country}}}","activeVariant":{"sku":{{{activeSku}}},"inStock":null},"defaultImages":[],"variants":[]}; const later = [1];</script></body></html>
        """;

    private static Task<IndirimTakip.Core.Scraping.ProductDetails> FetchDetails(string page, string url)
    {
        var handler = new RoutingHandler(new Dictionary<string, string> { [new Uri(url).PathAndQuery] = page });
        return new MyproteinScraper(new HttpClient(handler), NullLogger<MyproteinScraper>.Instance, TimeSpan.Zero)
            .FetchDetailsAsync(url);
    }

    // Measured 2026-10-07: the first table follows ?variation=, 80 g protein per 100 g for this flavour and
    // 82 g for the default one.
    [Fact]
    public async Task Details_are_the_linked_flavours_table()
    {
        var details = await FetchDetails(DetailPage("GB", 15049598), IsolateUrl + "?variation=15049598");

        Assert.Equal(24m, details.ProteinPerServingGrams);
        Assert.Equal(30m, details.ServingSizeGrams);
        Assert.Contains("\"Protein\":\"24g\"", details.NutritionJson);
    }

    // A SKU the store no longer sells opens the page on another variant: its first table is another flavour's.
    [Fact]
    public async Task A_page_served_for_another_variant_gives_no_details()
    {
        var details = await FetchDetails(DetailPage("GB", 17716566), IsolateUrl + "?variation=15049598");

        Assert.Null(details.NutritionJson);
        Assert.Null(details.ProteinPerServingGrams);
    }

    [Fact]
    public async Task Details_from_another_market_are_retried_later()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => FetchDetails(DetailPage("DE", 15049598), IsolateUrl + "?variation=15049598"));
    }

    private sealed class RoutingHandler(Dictionary<string, string> pages) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages.GetValueOrDefault(path, "<html></html>"), Encoding.UTF8, "text/html"),
                RequestMessage = request,
            });
        }
    }
}
