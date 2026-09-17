using IndirimTakip.Infrastructure.Scraping.Shopify;
using IndirimTakip.Infrastructure.Scraping.Woo;

namespace IndirimTakip.Infrastructure.Tests;

// Products mirror what Form's US Store API returned on 2026-09-18.
public class WooStoreScraperTests
{
    private static readonly WooStore Store = new("Form", "https://formnutrition.com/us");
    private static readonly WooStore SportOnly =
        new("Form", "https://formnutrition.com/us", ShopifyStores.SportCategories);

    private static WooProduct Product(
        string name, string price, string? currency = "USD", string type = "variable",
        string? category = null, (string Slug, string Label)[]? sizeTerms = null,
        string[]? variationSizes = null, string? regularPrice = null, bool purchasable = true) => new()
        {
            Id = 1,
            Name = name,
            Permalink = "https://formnutrition.com/us/p",
            Type = type,
            IsInStock = true,
            IsPurchasable = purchasable,
            Prices = new WooPrices
            {
                Price = price,
                RegularPrice = regularPrice ?? price,
                CurrencyCode = currency,
                CurrencyMinorUnit = 2,
            },
            Categories = category is null ? [] : [new WooTerm { Name = category, Slug = category }],
            Attributes = sizeTerms is null
                ? []
                : [new WooAttribute
                {
                    Name = "Size",
                    Terms = [.. sizeTerms.Select(t => new WooTerm { Name = t.Label, Slug = t.Slug })],
                }],
            Variations = variationSizes is null
                ? []
                : [.. variationSizes.Select((s, i) => new WooVariation
                {
                    Id = 100 + i,
                    Attributes = [new WooVariationAttribute { Name = "Size", Value = s }],
                })],
        };

    // THE TRAP THIS SCRAPER EXISTS AROUND: the Store API returns minor units.
    // "3900" is $39.00; taken as a number the site would show $3,900.
    [Fact]
    public void Price_comes_from_minor_units()
    {
        var p = Product("Performance Protein - Vegan Protein Powder", "3900", regularPrice: "4900");

        var item = Assert.Single(WooStoreScraper.ToScrapedProducts(p, Store));

        Assert.Equal(39.00m, item.Price);
        Assert.Equal(49.00m, item.StoreOldPrice);
    }

    // The store's own market switch is the failure that looks right: GBP amounts
    // published as dollars are plausible and wrong by the exchange rate.
    [Fact]
    public void Foreign_currency_is_refused_loudly()
    {
        var p = Product("Performance Protein", "3900", currency: "GBP");

        var error = Assert.Throws<InvalidOperationException>(
            () => WooStoreScraper.ToScrapedProducts(p, Store).ToList());

        Assert.Contains("GBP", error.Message);
    }

    // The attribute's term list is the SITE-WIDE taxonomy: Form's protein lists
    // three sizes there and sells one. Reading it would invent two packages.
    [Fact]
    public void Size_comes_from_the_variations_not_the_taxonomy()
    {
        var p = Product("Performance Protein - Vegan Protein Powder", "3900",
            sizeTerms: [("3x40g", "3 x 40g"), ("520g", "520g (13 servings)"), ("6-x-520g", "6 x 520g")],
            variationSizes: ["520g", "520g"]);

        var item = Assert.Single(WooStoreScraper.ToScrapedProducts(p, Store));

        Assert.Equal("Performance Protein - Vegan Protein Powder - 520g (13 servings)", item.Name);
    }

    [Theory]
    // A leftover test row and a bundle builder, both live in the first store's
    // catalog; the shaker is an accessory.
    [InlineData("Test product", "100", "variable")]
    [InlineData("Build Your Bundle", "0", "bundle")]
    [InlineData("Form Insulated Stainless Steel Shaker", "3200", "variable")]
    public void Non_products_are_skipped(string name, string price, string type)
    {
        var p = Product(name, price, type: type);

        Assert.Empty(WooStoreScraper.ToScrapedProducts(p, Store));
    }

    [Fact]
    public void Accessory_category_is_skipped_even_when_the_name_looks_fine()
    {
        var p = Product("Form Bottle 750ml", "3200", category: "Accessories");

        Assert.Empty(WooStoreScraper.ToScrapedProducts(p, Store));
    }

    [Fact]
    public void Category_rule_keeps_the_sport_range_and_drops_the_vitamins()
    {
        var protein = Product("Pureblend Protein - Unflavored Protein", "3900");
        var multi = Product("Multi - Vegan Multivitamin", "2900", category: "Capsules and Supplements");

        Assert.Single(WooStoreScraper.ToScrapedProducts(protein, SportOnly));
        Assert.Empty(WooStoreScraper.ToScrapedProducts(multi, SportOnly));
    }

    [Fact]
    public void A_product_that_cannot_be_bought_is_skipped()
    {
        var p = Product("Performance Protein", "3900", purchasable: false);

        Assert.Empty(WooStoreScraper.ToScrapedProducts(p, Store));
    }
}
