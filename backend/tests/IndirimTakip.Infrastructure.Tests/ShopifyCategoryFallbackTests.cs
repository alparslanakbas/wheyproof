using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Tests;

// Category fallbacks added with the 2026-09-14 stores. Each one only applies
// when the normal rule found nothing.
public class ShopifyCategoryFallbackTests
{
    private static ShopifyProduct Product(string title, string? type = null, params string[] tags) => new()
    {
        Title = title,
        Handle = "p",
        ProductType = type,
        Options = [new ShopifyOption { Name = "Title", Position = 1 }],
        Variants = [new ShopifyVariant { Id = 1, Option1 = "Default Title", Price = 40m, Available = true }],
        Images = [],
        Tags = tags.ToList(),
    };

    private static string? CategoryOf(ShopifyProduct product, ShopifyStore store) =>
        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(product, store, null)).Category;

    // Stripping "Ultimate Paleo Protein" left ", Vanilla - 15 Servings".
    [Fact]
    public void A_brand_named_after_the_product_still_gets_a_category() =>
        Assert.Equal("protein-powder",
            CategoryOf(Product("Ultimate Paleo Protein, Vanilla"), new ShopifyStore("Ultimate Paleo Protein", "https://ultimatepaleoprotein.com")));

    // The normal rule still wins: the brand word must not decide a creatine.
    [Fact]
    public void The_brand_word_does_not_override_a_category_found_without_it() =>
        Assert.Equal("creatine",
            CategoryOf(Product("Ultimate Paleo Protein Creatine"), new ShopifyStore("Ultimate Paleo Protein", "https://ultimatepaleoprotein.com")));

    [Theory]
    [InlineData("Animal Cuts", "Category: Fat Burners", "fat-burners")]
    [InlineData("Animal Fury", "Category:Pre Workout", "pre-workout")]
    public void Declared_category_tags_name_the_kind_when_the_title_does_not(string title, string tag, string expected) =>
        Assert.Equal(expected, CategoryOf(Product(title, "Pill Pack", "all_supps", tag), new ShopifyStore("Animal", "https://www.animalpak.com")));

    // Free marketing tags are ignored: in the 2026-09-14 crawl they filed Naked
    // Fiber as protein powder and let herbal extracts past BulkSupplements'
    // sport-only filter.
    [Theory]
    [InlineData("Organic Fiber Supplement | Naked Fiber", "Protein", "Pre-Workout")]
    [InlineData("Animal Nitro - 44 packs", "aminos_bcaa_eaa", "Category:Post Workout")]
    public void Free_tags_do_not_decide_a_category(string title, string tag1, string tag2) =>
        Assert.Null(CategoryOf(Product(title, null, tag1, tag2), new ShopifyStore("Naked Nutrition", "https://www.nakednutrition.com")));

    // Tags never replace a category the title already gave.
    [Fact]
    public void Tags_are_ignored_when_the_title_has_a_category() =>
        Assert.Equal("creatine",
            CategoryOf(Product("Animal Creatine Chews", null, "Category:Pre Workout"), new ShopifyStore("Animal", "https://www.animalpak.com")));

    // Some names carry no kind anywhere; they stay uncategorised, nothing guessed.
    [Fact]
    public void No_signal_anywhere_leaves_no_category() =>
        Assert.Null(CategoryOf(Product("Animal Pak Powder", "Other Powders", "all_supps", "Category:Wellness"), new ShopifyStore("Animal", "https://www.animalpak.com")));

    [Theory]
    [InlineData("Protein Bites - 20G - Churro", "protein-snacks")]
    [InlineData("Protein Candy - 20G - Sour Watermelon", "protein-snacks")]
    // "Candy" and "bites" alone are flavors, not snacks.
    [InlineData("Pre-Workout Gummies - Cotton Candy", "pre-workout")]
    [InlineData("Whey Protein Powder - Brownie Bites", "protein-powder")]
    public void Protein_snack_phrases_without_catching_flavors(string title, string expected) =>
        Assert.Equal(expected, ProductAttributeParser.InferCategory(title));
}
