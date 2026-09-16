using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Titles, handles and types mirror 33nutrition.com's products.json on 2026-09-17.
public class ShopifyOnlyHandlesTests
{
    private static readonly ShopifyStore Store = ShopifyStores.All.Single(s => s.BrandName == "33 Nutrition");

    private static ShopifyProduct Product(string title, string handle, string type, decimal price) => new()
    {
        Title = title,
        Handle = handle,
        ProductType = type,
        Options = [new ShopifyOption { Name = "Title", Position = 1 }],
        Variants = [new ShopifyVariant { Id = 1, Option1 = "Default Title", Price = price, Available = true }],
        Images = [],
    };

    [Theory]
    [InlineData("Complete Multivitamin", "complete-multivitamin", "Vitamins & Minerals", 20.68, "vitamins")]
    [InlineData("BCAA Recovery", "bcaa-recovery", "BCAA", 39.99, "amino-acids")]
    public void Listed_handles_are_kept_with_a_category(string title, string handle, string type, decimal price, string category)
    {
        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(Product(title, handle, type, price), Store, null));

        Assert.Equal(title, item.Name);
        Assert.Equal(price, item.Price);
        Assert.Equal(category, item.Category);
        Assert.Equal($"https://33nutrition.com/products/{handle}", item.Url);
    }

    // A fat burner would pass a category rule; the handle list keeps it out.
    [Theory]
    [InlineData("Night Time Fat Burner", "night-time-fat-burner", "Fat Burner")]
    [InlineData("Diet Drops", "diet-drops", "Diet Drops")]
    [InlineData("Chlorella", "chlorella", "Detox")]
    public void Other_products_are_dropped(string title, string handle, string type) =>
        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(Product(title, handle, type, 39.99m), Store, null));

    [Fact]
    public void Stores_without_a_handle_list_are_unaffected()
    {
        var other = new ShopifyStore("Nutricost", "https://nutricost.com");

        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(
            Product("Nutricost BCAA Powder", "bcaa-powder", "Amino Acids", 24.95m), other, null));
    }
}
