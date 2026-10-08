using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Rows the 2026-10-08 stores showed that are not a product's price: sold-out sizes
// at a closeout store, short-dated lots, a bundle-builder placeholder and the
// add-ins of a custom blend. Titles and prices mirror their products.json that day.
public class ShopifyClearanceRowTests
{
    private static readonly ShopifyStore Closeout = new("Supplement Hunt", "https://supplementhunt.com",
        IsRetailer: true, OnlyCategories: ShopifyStores.SportCategories, OnlyInStock: true);

    private static readonly ShopifyStore Brand = new("RYSE", "https://rysesupps.com");

    private static ShopifyProduct Product(string title, string? type, string? vendor = null,
        params (long Id, string Size, decimal Price, bool Available)[] variants) => new()
    {
        Title = title,
        Handle = "p",
        ProductType = type,
        Vendor = vendor,
        Options = [new ShopifyOption { Name = "Size", Position = 1 }],
        Variants = variants.Select(v => new ShopifyVariant
        {
            Id = v.Id, Option1 = v.Size, Price = v.Price, Available = v.Available,
        }).ToList(),
        Images = [],
    };

    [Fact]
    public void Closeout_store_keeps_only_sizes_in_stock()
    {
        var p = Product("Rule1 Mass Gainer", "Protein", "Rule 1",
            (1, "6lbs", 39.99m, true),
            (2, "12lbs", 69.99m, false));

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Closeout, "supplementhunt.com"));

        Assert.Contains("6lbs", item.Name);
        Assert.True(item.InStock);
    }

    [Fact]
    public void Other_stores_still_list_sold_out_sizes()
    {
        var p = Product("Clear Whey Protein", "Protein", null,
            (1, "20 Servings", 39.99m, true),
            (2, "40 Servings", 69.99m, false));

        Assert.Equal(2, ShopifyStoreScraper.ToScrapedProducts(p, Brand, null).Count());
    }

    [Fact]
    public void Short_dated_size_is_skipped()
    {
        var p = Product("AllMax CLA Weight Loss Support 30 Softgels", "Fat Burner", "AllMax",
            (1, "30ct (Best Before Oct 1, 2026)", 4.99m, true),
            (2, "90ct", 19.99m, true));

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Closeout, "supplementhunt.com"));

        Assert.DoesNotContain("Best Before", item.Name);
    }

    [Fact]
    public void Bundle_builder_placeholder_is_skipped() =>
        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(
            Product("Build Your Bundle!", "Protein", null, (1, "Default Title", 3.33m, true)), Brand, null));

    [Theory]
    [InlineData("Beef Collagen Boost", "Boost")]
    [InlineData("Cinnamon Flavor", "Flavor")]
    [InlineData("1st Phorm App Access", "App")]
    [InlineData("NASM Certification", "Certification")]
    public void Add_ins_and_services_are_skipped(string title, string type) =>
        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(
            Product(title, type, null, (1, "Default Title", 1.00m, true)), Brand, null));
}
