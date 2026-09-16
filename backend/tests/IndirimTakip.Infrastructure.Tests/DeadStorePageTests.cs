using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Rows the store lists but whose US product page doesn't exist. Tags and
// handles mirror Naked Nutrition's products.json on 2026-09-16.
public class DeadStorePageTests
{
    private static readonly ShopifyStore Naked = new("Naked Nutrition", "https://www.nakednutrition.com");

    private static ShopifyProduct Product(string title, params string[] tags) => new()
    {
        Title = title,
        Handle = "p",
        ProductType = "Protein Powder",
        Tags = [.. tags],
        Options = [new ShopifyOption { Name = "Title", Position = 1 }],
        Variants = [new ShopifyVariant { Id = 1, Option1 = "Default Title", Price = 19.99m, Available = true }],
        Images = [],
    };

    [Fact]
    public void Eu_and_uk_editions_are_not_published()
    {
        var p = Product("Pea Protein Powder | Naked Pea - 450g", "1LB", "Protein Powder", "market-eu", "market-uk");

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Naked, null));
    }

    [Fact]
    public void A_product_without_market_tags_stays()
    {
        var p = Product("Pea Protein Powder | Naked Pea - 5LB", "5LB", "Best Seller", "Protein Powder");

        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Naked, null));
    }

    [Theory]
    [InlineData(new[] { "market-eu", "market-uk" }, true)]
    [InlineData(new[] { "Market-EU" }, true)]
    [InlineData(new[] { "market-eu", "market-us" }, false)]
    [InlineData(new[] { "market-us" }, false)]
    [InlineData(new[] { "Best Seller", "Canada", "export_to_ca" }, false)]
    [InlineData(new string[0], false)]
    public void Only_products_tagged_solely_for_other_markets_are_dropped(string[] tags, bool expected) =>
        Assert.Equal(expected, ShopifyStoreScraper.IsOtherMarketOnly(tags));

    private static readonly DateTimeOffset First = new(2026, 9, 15, 18, 47, 0, TimeSpan.Zero);

    [Fact]
    public void First_404_only_records_the_time()
    {
        var product = new Product { Name = "Pea Protein Powder | Naked Pea - 450g", Url = "https://www.nakednutrition.com/products/pea-protein-powder-450g", IsActive = true };

        Assert.False(ProductDetailBackfillService.RecordPageNotFound(product, First));
        Assert.Equal(First, product.PageNotFoundAt);
        Assert.True(product.IsActive);
    }

    // A manual re-run minutes later must not count as the second check.
    [Fact]
    public void A_second_404_soon_after_does_not_hide()
    {
        var product = new Product { Name = "Pea Protein Powder | Naked Pea - 450g", Url = "https://www.nakednutrition.com/products/pea-protein-powder-450g", IsActive = true, PageNotFoundAt = First };

        Assert.False(ProductDetailBackfillService.RecordPageNotFound(product, First.AddHours(1)));
        Assert.True(product.IsActive);
        Assert.Equal(First, product.PageNotFoundAt);
    }

    [Fact]
    public void A_404_again_the_next_day_hides_the_product()
    {
        var product = new Product { Name = "Pea Protein Powder | Naked Pea - 450g", Url = "https://www.nakednutrition.com/products/pea-protein-powder-450g", IsActive = true, PageNotFoundAt = First };

        Assert.True(ProductDetailBackfillService.RecordPageNotFound(product, First.AddDays(1)));
        Assert.False(product.IsActive);
    }
}
