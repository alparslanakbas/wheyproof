using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Magento;

namespace IndirimTakip.Infrastructure.Tests;

// Shapes mirror bulk.com's storefront GraphQL and UK sitemap on 2026-09-19.
public class MagentoStoreScraperTests
{
    private static readonly MagentoStore Bulk = new(
        "Bulk", "https://www.bulk.com", "https://www.bulk.com/media/feeds/sitemapUK.xml", "/uk/products/",
        Market: SiteMarket.Uk);

    private const string Url = "https://www.bulk.com/uk/products/pure-whey-protein/bpb-wpc8-0000";

    private static MagentoVariant Variant(string flavour, int sizeIndex, string size, decimal price, decimal regular,
        bool inStock = true, string currency = "GBP", string sizeCode = "bp_size") => new()
    {
        Attributes =
        [
            new MagentoVariantAttribute { Code = "bp_flavour", Label = flavour, ValueIndex = 1 },
            new MagentoVariantAttribute { Code = sizeCode, Label = size, ValueIndex = sizeIndex },
        ],
        Product = new MagentoVariantProduct
        {
            StockStatus = inStock ? "IN_STOCK" : "OUT_OF_STOCK",
            PriceRange = new MagentoPriceRange
            {
                MinimumPrice = new MagentoPrice
                {
                    FinalPrice = new MagentoMoney { Value = price, Currency = currency },
                    RegularPrice = new MagentoMoney { Value = regular },
                },
            },
        },
    };

    private static MagentoProduct Product(string name, string sizeCode, params MagentoVariant[] variants) => new()
    {
        Name = name,
        UrlKey = "pure-whey-protein",
        ConfigurableOptions =
        [
            new MagentoOption { AttributeCode = "bp_flavour", AttributeId = "178" },
            new MagentoOption { AttributeCode = sizeCode, AttributeId = "179" },
        ],
        Variants = [.. variants],
    };

    [Fact]
    public void Sitemap_gives_each_products_url_key()
    {
        const string sitemap = """
            <urlset><url><loc>https://www.bulk.com/uk/products/zma-capsules/bpb-zma-0000</loc></url>
            <url><loc>https://www.bulk.com/uk/products/pure-whey-protein/bpb-wpc8-0000</loc></url>
            <url><loc>https://www.bulk.com/uk/the-core/some-article</loc></url></urlset>
            """;

        var urls = MagentoStoreScraper.ProductUrlsByKey(sitemap, "/uk/products/");

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://www.bulk.com/uk/products/zma-capsules/bpb-zma-0000", urls["zma-capsules"]);
    }

    // Each size its own product; flavours collapse into the cheapest in stock,
    // because an out-of-stock flavour can't be bought.
    [Fact]
    public void Each_size_is_a_product_priced_at_its_cheapest_flavour_in_stock()
    {
        var product = Product("Pure Whey Protein™", "bp_size",
            Variant("Chocolate", 25, "1kg", 32.99m, 38.99m),
            Variant("Vanilla", 25, "1kg", 29.99m, 38.99m, inStock: false),
            Variant("Banana", 25, "1kg", 31.49m, 38.99m),
            Variant("Chocolate", 33, "2.5kg", 77.99m, 89.99m));

        var rows = MagentoStoreScraper.ToScrapedProducts(product, Url, Bulk).ToList();

        Assert.Equal(2, rows.Count);
        var oneKg = Assert.Single(rows, r => r.Name == "Pure Whey Protein - 1kg");
        Assert.Equal(31.49m, oneKg.Price);
        Assert.Equal(38.99m, oneKg.StoreOldPrice);
        Assert.True(oneKg.InStock);
        // Magento's own option link: base64("179-25").
        Assert.Equal($"{Url}?o=MTc5LTI1", oneKg.Url);
        Assert.Equal(2, rows.Select(r => r.Url).Distinct().Count());
    }

    // "Caffeine 200mg 100 Tablets" sells 100 and 250 tablets: appending to the
    // raw name gave "... 100 Tablets - 250 Tablets".
    [Fact]
    public void A_size_already_in_the_name_is_not_doubled()
    {
        var product = Product("Caffeine 200mg 100 Tablets", "bp_qty",
            Variant("Unflavoured", 79, "100 Tablets", 4.99m, 6.99m, sizeCode: "bp_qty"),
            Variant("Unflavoured", 81, "250 Tablets", 11.49m, 16.99m, sizeCode: "bp_qty"));

        var names = MagentoStoreScraper.ToScrapedProducts(product, Url, Bulk).Select(r => r.Name).ToList();

        Assert.Equal(["Caffeine 200mg - 100 Tablets", "Caffeine 200mg - 250 Tablets"], names);
    }

    // A size label must match as a whole word: "500g" is not part of "1500g".
    [Fact]
    public void Removing_a_size_from_the_name_needs_a_whole_word() =>
        Assert.Equal("Mass Gainer 1500g", MagentoStoreScraper.WithoutSizeLabels("Mass Gainer 1500g", ["500g"]));

    // The UK section reads GBP; euros shown as pounds would look plausible and be wrong.
    [Fact]
    public void Another_currency_is_refused_loudly()
    {
        var product = Product("Pure Whey Protein", "bp_size", Variant("Chocolate", 25, "1kg", 32.99m, 38.99m, currency: "EUR"));

        Assert.Throws<InvalidOperationException>(() => MagentoStoreScraper.ToScrapedProducts(product, Url, Bulk).ToList());
    }

    [Theory]
    [InlineData("Barista Syrup Dispensing Pump")]
    [InlineData("Syrup Pump - 1L")]
    public void A_syrup_pump_is_not_a_pre_workout(string name) =>
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));

    [Fact]
    public void A_pump_pre_workout_stays() =>
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel("Pump Pre-Workout - 500g"));
}
