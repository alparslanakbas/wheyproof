using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Auri labels its 3-pack "3-Pack ($34.99 ea) / 90 Day Supply" while the one-time
// 3-pack costs $165 (2026-09-19): the note is the subscription price per bag.
public class ShopifyPriceNoteTests
{
    private static readonly ShopifyStore Store = new("Auri Nutrition", "https://www.tryauri.com");

    private static ShopifyProduct Product(string option, decimal price) => new()
    {
        Title = "Super Mushroom Focus Gummies",
        Handle = "super-mushroom-focus-gummies",
        ProductType = "Vitamins & Supplements",
        Tags = [],
        Options = [new ShopifyOption { Name = "Choose Your Bundle:", Position = 1 }],
        Variants = [new ShopifyVariant { Id = 1, Option1 = option, Price = price, Available = true }],
        Images = [],
    };

    [Theory]
    [InlineData("3-Pack ($34.99 ea) / 90 Day Supply", "Super Mushroom Focus Gummies - 3-Pack / 90 Day Supply")]
    [InlineData("3-Pack (34.99 each)", "Super Mushroom Focus Gummies - 3-Pack")]
    [InlineData("1-Pack / 30 Day Supply", "Super Mushroom Focus Gummies - 1-Pack / 30 Day Supply")]
    public void A_price_note_in_an_option_label_leaves_the_name(string option, string expected)
    {
        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(Product(option, 165m), Store, null));

        Assert.Equal(expected, item.Name);
        Assert.Equal(165m, item.Price);
    }

    // A size that happens to sit in parentheses is not a price note.
    [Fact]
    public void A_size_in_parentheses_stays()
    {
        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(Product("Large (2 lb)", 49.99m), Store, null));

        Assert.EndsWith("Large (2 lb)", item.Name);
    }
}
