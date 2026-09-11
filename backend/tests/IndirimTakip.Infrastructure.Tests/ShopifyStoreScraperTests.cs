using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Products mirror what the stores returned on 2026-09-11.
public class ShopifyStoreScraperTests
{
    private static readonly ShopifyStore Brand = new("Nutricost", "https://nutricost.com");
    private static readonly ShopifyStore Retailer = new("Bodybuilding.com", "https://www.bodybuilding.com", IsRetailer: true);

    private static ShopifyProduct Product(
        string title, string? type, string[] optionNames,
        params (long Id, string? Option1, string? Option2, decimal Price, bool Available)[] variants) => new()
    {
        Title = title,
        Handle = "p",
        ProductType = type,
        Options = optionNames.Select((n, i) => new ShopifyOption { Name = n, Position = i + 1 }).ToList(),
        Variants = variants.Select(v => new ShopifyVariant
        {
            Id = v.Id, Option1 = v.Option1, Option2 = v.Option2, Price = v.Price, Available = v.Available,
        }).ToList(),
        Images = [],
    };

    // Nutricost whey: Flavor x Size on one page, sizes priced differently.
    // Taking one variant would publish an arbitrary size's price.
    [Fact]
    public void Each_size_is_a_product_and_flavors_collapse()
    {
        var p = Product("Nutricost Whey Protein Concentrate Powder", null, ["Flavor", "Size"],
            (1, "Vanilla", "1.5 lb", 39.97m, true),
            (2, "Vanilla", "5 lbs", 89.97m, true),
            (3, "Chocolate", "1.5 lb", 39.97m, true),
            (4, "Chocolate", "5 lbs", 84.97m, false));

        var items = ShopifyStoreScraper.ToScrapedProducts(p, Brand, null).ToList();

        Assert.Equal(2, items.Count);
        var fiveLb = Assert.Single(items, i => i.Name.EndsWith("5 lbs"));
        // The cheaper flavor is out of stock, so it cannot set the price.
        Assert.Equal(89.97m, fiveLb.Price);
        Assert.Contains("?variant=2", fiveLb.Url);
        Assert.Equal("5 lb", ProductAttributeParser.ExtractSize(fiveLb.Name));
    }

    [Fact]
    public void Product_without_options_keeps_a_plain_url()
    {
        var p = Product("Creatine Capsules | Naked Creatine - 75 Servings", "Extras", ["Title"],
            (10, "Default Title", null, 24.99m, true));

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));

        Assert.Equal("https://nutricost.com/products/p", item.Url);
        Assert.Equal("Creatine Capsules | Naked Creatine - 75 Servings", item.Name);
        Assert.Equal(75, item.ServingsPerPackage);
    }

    // BulkSupplements sells the same ingredient as capsules and as powder.
    [Fact]
    public void Style_and_size_both_split_products()
    {
        var p = Product("Zinc Orotate Capsules", "Zinc Mineral", ["Style", "Size"],
            (20, "Capsule", "240 Capsules", 22.96m, true),
            (21, "Powder", "100 Grams (3.5 oz)", 19.96m, true));

        var items = ShopifyStoreScraper.ToScrapedProducts(p, Brand, null).ToList();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Name == "Zinc Orotate Capsules - Capsule / 240 Capsules");
        Assert.Contains(items, i => ProductAttributeParser.ExtractSize(i.Name) == "100 g");
    }

    [Theory]
    [InlineData("RAW Sport Tank Top", "Apparel")]
    [InlineData("GHOST® ATHLETICS TEE | GRANITE", "ATHLETICS")]
    [InlineData("Division Socks", "")]
    [InlineData("Nutricost Pantry Fine Ground Black Pepper", "")]
    [InlineData("Gift Card", "Gift Card")]
    public void Non_supplements_are_dropped(string title, string type)
    {
        var p = Product(title, type, ["Title"], (30, "Default Title", null, 25m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    [Fact]
    public void Caps_as_capsules_is_not_merch()
    {
        var p = Product("Ashwagandha 120 Caps", "", ["Title"], (40, "Default Title", null, 15m, true));

        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    // MuscleTech listed a product at 0.00; it would divide by zero downstream.
    [Fact]
    public void Zero_price_variants_are_skipped()
    {
        var p = Product("My Peptide System", "", ["Title"], (50, "Default Title", null, 0m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    [Fact]
    public void Retailer_products_take_brand_from_vendor()
    {
        var p = Product("Elev8 Creamy Rice Georgia Peach", "Carb Powders", ["Flavor"],
            (60, "Georgia Peach", null, 25.99m, true));
        p.Vendor = "Elev8";

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Retailer, "bodybuilding.com"));

        Assert.Equal("Elev8", item.BrandName);
        Assert.Equal("bodybuilding.com", item.Seller);
        Assert.Equal("mass-gainers", item.Category);
    }

    [Theory]
    [InlineData("<script>Shopify.currency = {\"active\":\"EUR\",\"rate\":\"0.92\"};</script>", "EUR")]
    [InlineData("<script>Shopify.currency = {\"active\":\"USD\",\"rate\":\"1.0\"};</script>", "USD")]
    [InlineData("<html>no marker</html>", null)]
    public void Reads_storefront_currency(string html, string? expected)
    {
        Assert.Equal(expected, ShopifyStoreScraper.ParseStorefrontCurrency(html));
    }
}
