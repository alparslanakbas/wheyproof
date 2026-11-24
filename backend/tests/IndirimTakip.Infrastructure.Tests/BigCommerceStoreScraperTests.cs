using System.Globalization;
using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping.BigCommerce;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// BigCommerce product pages (A1 Supplements). The markup follows the store's real
/// pages as measured on 2026-11-24: a schema.org Product with an Offer, the
/// storefront's own <c>var BCData = {"product_attributes":...}</c>, and the size or
/// flavour choices in a select whose options carry data-product-attribute-value.
/// </summary>
public class BigCommerceStoreScraperTests
{
    private static readonly BigCommerceStore A1 =
        new("A1 Supplements", "https://www.a1supplements.com", OnlyCategories: ShopifyStores.SportCategories,
            OwnLabelPrefixes: ["A1 "]);

    private const string Seller = "a1supplements.com";

    private static string Page(
        string name,
        string brand,
        decimal price,
        string currency = "USD",
        bool inStock = true,
        string[]? options = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        decimal? nonSalePrice = null,
        string slug = "x")
    {
        // Placeholders, not interpolation: the JSON's own braces would clash with it, and
        // a decimal formatted in the machine's culture (tr-TR: "19,99") would break the JSON.
        static string Num(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

        var range = minPrice is null ? "" : $",\"minPrice\":\"{Num(minPrice)}\",\"maxPrice\":\"{Num(maxPrice)}\"";
        var nonSale = nonSalePrice is null
            ? ""
            : $",\"non_sale_price_without_tax\":{{\"formatted\":\"${Num(nonSalePrice)}\",\"value\":{Num(nonSalePrice)},\"currency\":\"{currency}\"}}";
        var optionTags = string.Join("\n", (options ?? []).Select((label, i) =>
            $"<option data-product-attribute-value=\"{427 + i}\" value=\"{427 + i}\">{label}</option>"));

        return """
            <!DOCTYPE html><html lang="en"><head>
            <meta property="og:image" content="https://cdn11.bigcommerce.com/s-x/products/1/images/2/og.jpg">
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"Organization","name":"A1 Supplements"}]}</script>
            <script type="application/ld+json">{"@context":"https://schema.org/","@type":"Product","@id":"https://a1supplements.com/@SLUG@","name":"@NAME@","sku":"@SLUG@-1","url":"https://a1supplements.com/@SLUG@","brand":{"@type":"Brand","url":"https://a1supplements.com/b","name":"@BRAND@"},"image":"https://cdn11.bigcommerce.com/s-x/images/stencil/1280x1280/products/1/2/@SLUG@.jpg","offers":{"@type":"Offer","priceCurrency":"@CURRENCY@","price":"@PRICE@"@RANGE@,"availability":"https://schema.org/@AVAILABILITY@","url":"https://a1supplements.com/@SLUG@"}}</script>
            </head><body>
            <select class="form-select" name="attribute[1]" data-product-attribute="set-select">
            <option value="">Choose Options</option>
            @OPTIONS@
            </select>
            <script>var BCData = {"product_attributes":{"sku":"@SLUG@-1","upc":null,"price":{"without_tax":{"formatted":"$@PRICE@","value":@PRICE@,"currency":"@CURRENCY@"},"tax_label":"Tax"@NONSALE@},"instock":@STOCK@,"purchasable":true}};</script>
            </body></html>
            """
            .Replace("@SLUG@", slug)
            .Replace("@NAME@", name)
            .Replace("@BRAND@", brand)
            .Replace("@CURRENCY@", currency)
            .Replace("@PRICE@", Num(price))
            .Replace("@RANGE@", range)
            .Replace("@AVAILABILITY@", inStock ? "InStock" : "OutOfStock")
            .Replace("@OPTIONS@", optionTags)
            .Replace("@NONSALE@", nonSale)
            .Replace("@STOCK@", inStock ? "true" : "false");
    }

    private static BigCommerceStoreScraper.Outcome Read(string html) =>
        BigCommerceStoreScraper.ToScrapedProduct(
            BigCommerceStoreScraper.ParseProductPage(html, "https://a1supplements.com/x")!, A1, Seller);

    [Fact]
    public void Name_brand_price_stock_and_the_single_size_are_read()
    {
        var product = Read(Page("Kaged Muscle BCAA 2:1:1", "Kaged Muscle", 19.99m, inStock: false,
            options: ["Unflavored 200 Grams"])).Product;

        Assert.NotNull(product);
        Assert.Equal("Kaged Muscle BCAA 2:1:1 - 200 Grams", product.Name);
        Assert.Equal("Kaged Muscle", product.BrandName);
        Assert.Equal(19.99m, product.Price);
        Assert.Equal("amino-acids", product.Category);
        Assert.False(product.InStock); // kept: prices are tracked through restocks
        Assert.Equal(Seller, product.Seller);
        Assert.Equal("https://a1supplements.com/x", product.Url);
        Assert.Equal("https://cdn11.bigcommerce.com/s-x/images/stencil/1280x1280/products/1/2/x.jpg", product.ImageUrl);
        Assert.Null(product.StoreOldPrice);
    }

    /// <summary>Flavours repeat the one size; it goes into the name once.</summary>
    [Fact]
    public void Flavours_that_share_a_size_name_that_size()
    {
        var product = Read(Page("Ayone Nutrition Pre-Workout", "Ayone Nutrition", 34.95m,
            options: ["Bombsicle Splash 30 Servings", "Cherry Lemonade Splash 30 Servings", "Grape Splash 30 Servings"])).Product;

        Assert.NotNull(product);
        Assert.Equal("Ayone Nutrition Pre-Workout - 30 Servings", product.Name);
        Assert.Equal("pre-workout", product.Category);
        Assert.Equal(30, product.ServingsPerPackage);
    }

    [Fact]
    public void The_non_sale_price_above_the_price_is_the_store_old_price()
    {
        var product = Read(Page("Ayone Nutrition Pre-Workout", "Ayone Nutrition", 59.95m, nonSalePrice: 79.95m)).Product;

        Assert.Equal(79.95m, product!.StoreOldPrice);
    }

    /// <summary>
    /// "29.95 - 79.95" with three pack sizes: the page doesn't say which size costs
    /// what, so the product is not priced from one end of the range.
    /// </summary>
    [Fact]
    public void A_price_range_is_skipped()
    {
        var outcome = Read(Page("Redcon1 Isotope Whey Isolate", "Redcon1", 49.99m, minPrice: 49.99m, maxPrice: 99.99m,
            options: ["Chocolate 30 Servings", "Chocolate 71 Servings"]));

        Assert.Null(outcome.Product);
        Assert.Equal(BigCommerceStoreScraper.SkipReason.PriceRange, outcome.Reason);
    }

    [Fact]
    public void Expired_stock_is_skipped()
    {
        var outcome = Read(Page("Now L-Carnitine Liquid 1000mg", "Now Sports", 18.39m, options: ["Punch 16 Oz. Exp. 2/2024"]));

        Assert.Equal(BigCommerceStoreScraper.SkipReason.Expired, outcome.Reason);
    }

    [Fact]
    public void Accessories_and_products_outside_the_sport_categories_are_skipped()
    {
        Assert.Equal(BigCommerceStoreScraper.SkipReason.Accessory,
            Read(Page("Psycho Pharma Shaker Cup", "Psycho Pharma", 6.99m)).Reason);
        Assert.Equal(BigCommerceStoreScraper.SkipReason.Category,
            Read(Page("Now Super Enzymes", "Now", 19.19m, options: ["90 Capsules"])).Reason);
    }

    [Fact]
    public void A_product_without_a_brand_is_skipped()
    {
        Assert.Equal(BigCommerceStoreScraper.SkipReason.NoBrand,
            Read(Page("Storm Pre-Workout", "", 49.99m)).Reason);
    }

    /// <summary>Live on 2026-11-24: GAT Sport's CLA filed under the store's own label.</summary>
    [Fact]
    public void The_retailers_own_label_is_not_a_brand()
    {
        Assert.Equal(BigCommerceStoreScraper.SkipReason.OwnLabel,
            Read(Page("GAT Sport CLA 1250", "A1 Supplements Store", 22.39m, options: ["90 Softgels"])).Reason);
    }

    /// <summary>
    /// "180 Gels" are softgels; read from the full name, the product became an
    /// energy gel (live on 2026-11-24). The category comes from the product's name.
    /// </summary>
    [Fact]
    public void The_category_comes_from_the_name_not_the_option_label()
    {
        var product = Read(Page("Now CLA 800 mg", "Now Sports", 23.79m, options: ["180 Gels"])).Product;

        Assert.NotNull(product);
        Assert.Equal("Now CLA 800 mg - 180 Gels", product.Name);
        Assert.Equal("fat-burners", product.Category);
    }

    [Theory]
    [InlineData("Peanut Butter 25 Srv.", "25 Srv.")]
    [InlineData("Unflavored 200 Grams", "200 Grams")]
    [InlineData("2 fl. oz.", "2 fl. oz.")]
    [InlineData("50 Veg Capsules", "50 Veg Capsules")]
    [InlineData("Tropical, 20 Servings", "20 Servings")]
    public void One_size_is_read_from_the_options(string label, string expected)
    {
        Assert.Equal(expected, BigCommerceStoreScraper.SingleSize([label]));
    }

    [Fact]
    public void Two_sizes_or_none_give_no_size()
    {
        Assert.Null(BigCommerceStoreScraper.SingleSize(["Chocolate 30 Servings", "Chocolate 71 Servings"]));
        Assert.Null(BigCommerceStoreScraper.SingleSize(["Animal Pak, Juiced Aminos, Animal Greens"]));
    }

    [Fact]
    public async Task The_sitemap_is_read_then_each_product_page()
    {
        var handler = new StubHandler(new()
        {
            ["https://www.a1supplements.com/xmlsitemap.php?type=products&page=1"] =
                "<urlset><url><loc>https://a1supplements.com/a</loc></url><url><loc>https://a1supplements.com/b</loc></url></urlset>",
            ["https://www.a1supplements.com/xmlsitemap.php?type=products&page=2"] = "<urlset></urlset>",
            ["https://a1supplements.com/a"] = Page("Kaged Muscle BCAA 2:1:1", "Kaged Muscle", 19.99m, slug: "a"),
            ["https://a1supplements.com/b"] = Page("Now Super Enzymes", "Now", 19.19m, slug: "b"),
        });
        var scraper = new BigCommerceStoreScraper(new HttpClient(handler), A1,
            NullLogger<BigCommerceStoreScraper>.Instance, TimeSpan.Zero);

        var products = await scraper.ScrapeAsync();

        Assert.Equal("https://a1supplements.com/a", Assert.Single(products).Url);
        Assert.Equal(
            [
                "https://www.a1supplements.com/xmlsitemap.php?type=products&page=1",
                "https://www.a1supplements.com/xmlsitemap.php?type=products&page=2",
                "https://a1supplements.com/a",
                "https://a1supplements.com/b",
            ],
            handler.Requested);
    }

    /// <summary>Pounds shown as dollars would look plausible and be wrong.</summary>
    [Fact]
    public async Task A_page_in_another_currency_fails_loudly()
    {
        var handler = new StubHandler(new()
        {
            ["https://www.a1supplements.com/xmlsitemap.php?type=products&page=1"] =
                "<urlset><url><loc>https://a1supplements.com/a</loc></url></urlset>",
            ["https://www.a1supplements.com/xmlsitemap.php?type=products&page=2"] = "<urlset></urlset>",
            ["https://a1supplements.com/a"] = Page("Kaged Muscle BCAA 2:1:1", "Kaged Muscle", 15.99m, currency: "GBP", slug: "a"),
        });
        var scraper = new BigCommerceStoreScraper(new HttpClient(handler), A1,
            NullLogger<BigCommerceStoreScraper>.Instance, TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());

        Assert.Contains("answered in GBP, USD expected", error.Message);
    }

    private sealed class StubHandler(Dictionary<string, string> pages) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            return Task.FromResult(pages.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
