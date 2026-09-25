using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// The shared check every source passes (security/architecture review,
/// 2026-09-26). Written per scraper the rules drifted: the shared Shopify
/// scraper had no zero-price guard.
/// </summary>
public class ScrapedProductGuardTests
{
    private static ScrapedProduct Product(
        decimal price = 49.99m,
        string url = "https://www.nakednutrition.com/products/whey",
        string? image = "https://cdn.shopify.com/a.jpg",
        string? category = "protein-powder") =>
        new("Naked Whey 5 lb", url, image, category, price);

    [Fact]
    public void Valid_record_passes_unchanged()
    {
        var product = Product();

        Assert.Same(product, ScrapedProductGuard.Clean(product));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_negative_price_is_dropped(decimal price)
    {
        Assert.Null(ScrapedProductGuard.Clean(Product(price: price)));
    }

    [Theory]
    [InlineData("http://www.nakednutrition.com/products/whey")]
    [InlineData("/products/whey")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void Non_https_product_url_is_dropped(string url)
    {
        Assert.Null(ScrapedProductGuard.Clean(Product(url: url)));
    }

    [Theory]
    [InlineData("http://cdn.example.com/a.jpg")]
    [InlineData("//cdn.example.com/a.jpg")]
    [InlineData("data:image/png;base64,AAAA")]
    public void Non_https_image_is_cleared_but_the_product_stays(string image)
    {
        var result = ScrapedProductGuard.Clean(Product(image: image));

        Assert.NotNull(result);
        Assert.Null(result.ImageUrl);
        Assert.Equal(49.99m, result.Price);
    }

    /// <summary>
    /// A raw source label ("Protein Powder", "Supplements") appears on no
    /// category page; once cleared, name-based inference takes over.
    /// </summary>
    [Theory]
    [InlineData("Protein Powder")]
    [InlineData("Supplements")]
    [InlineData("")]
    public void Category_outside_the_list_is_cleared(string category)
    {
        var result = ScrapedProductGuard.Clean(Product(category: category));

        Assert.NotNull(result);
        Assert.Null(result.Category);
    }

    [Fact]
    public void Record_without_category_or_image_passes()
    {
        var result = ScrapedProductGuard.Clean(Product(image: null, category: null));

        Assert.NotNull(result);
        Assert.Null(result.ImageUrl);
        Assert.Null(result.Category);
    }
}
