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

    // Raw Nutrition stack: two qualified flavor options, no size at all. Each
    // flavor combination used to count as a size and became its own product.
    [Fact]
    public void Qualified_flavor_option_names_collapse_into_one_product()
    {
        var p = Product("Maximum Output Stack", null, ["Thavage Pre-Workout (Flavor)", "Creatine + HMB (Flavor)"],
            (30, "Champion Mentality", "Blue Raspberry", 97.47m, true),
            (31, "Champion Mentality", "Sour Watermelon", 86.22m, true),
            (32, "Lemon Lime", "Blue Raspberry", 97.47m, true));

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));

        Assert.Equal("Maximum Output Stack", item.Name);
        Assert.Equal("https://nutricost.com/products/p", item.Url);
        Assert.Equal(86.22m, item.Price);
    }

    [Theory]
    [InlineData("SavedBy Package Protection", false)]
    [InlineData("Shipping Insurance", false)]
    [InlineData("Bodybuilding.com Membership", false)]
    [InlineData("Protein Sample Packs", true)]
    [InlineData("Whey Protein Isolate", true)]
    public void Checkout_add_ons_are_not_products(string title, bool kept)
    {
        var p = Product(title, null, ["Title"], (50, "Default Title", null, 4.47m, true));

        Assert.Equal(kept, ShopifyStoreScraper.ToScrapedProducts(p, Brand, null).Any());
    }

    [Theory]
    [InlineData("Flavor", true)]
    [InlineData("Protein Flavor", true)]
    [InlineData("Whey Isolate Flavor - 30 servings/bag", true)]
    [InlineData("Thavage Pre-Workout (Flavour)", true)]
    [InlineData("Size", false)]
    [InlineData("Package Size", false)]
    [InlineData("Style", false)]
    public void Flavor_options_are_recognized_as_a_word(string optionName, bool expected) =>
        Assert.Equal(expected, ShopifyStoreScraper.IsFlavorOption(optionName));

    // The URL is the row's identity. When the cheapest flavor of a size sells
    // out, the price may change but the link must not, or the next crawl
    // opens a duplicate product.
    [Fact]
    public void Size_url_stays_the_same_when_the_cheapest_flavor_changes()
    {
        var before = Product("Whey", null, ["Flavor", "Size"],
            (40, "Vanilla", "5 lbs", 89.97m, true),
            (41, "Chocolate", "5 lbs", 79.97m, true));
        var after = Product("Whey", null, ["Flavor", "Size"],
            (40, "Vanilla", "5 lbs", 89.97m, true),
            (41, "Chocolate", "5 lbs", 79.97m, false));

        var first = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(before, Brand, null));
        var second = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(after, Brand, null));

        Assert.Equal(first.Url, second.Url);
        Assert.Equal(79.97m, first.Price);
        Assert.Equal(89.97m, second.Price);
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
    // Leaks found in the first full local crawl.
    [InlineData("Men's Dry Shirt - Large", "")]
    [InlineData("GHOST® CORE SHORT SLEEVE BUTTON DOWN | CHAMBRAY", "")]
    [InlineData("Endurance Swim Cap - Grey/Black", "")]
    [InlineData("Ascent JUNK Headband", "")]
    [InlineData("NAKED C.C. Dad Cap - Blue", "")]
    [InlineData("RAW Mug", "")]
    [InlineData("GHOST® RETRO COOLER", "")]
    [InlineData("Cooler Bag", "")]
    [InlineData("Nutricost Trimr Classic Bottle (Black)", "")]
    [InlineData("RAW Sport Bottle - 750ml", "")]
    [InlineData("Enamel Metal Cup 16 oz", "")]
    [InlineData("Empty Capsules - Clear - Size 0", "")]
    [InlineData("Bodybuilding.com Contour Nylon Weightlifting Belt", "")]
    [InlineData("Shipping Protection", "")]
    [InlineData("Free Shipping", "")]
    // Leaks found in the 2026-09-14 candidate stores.
    [InlineData("Animal Built. Not Born. Shaker", "Apparel and Accessories")]
    [InlineData("Animal Gym Duffle Bag Gray with Yellow A Logo", "")]
    [InlineData("Mystery Bottle ($39 value)", "")]
    [InlineData("Bounce Classic Logo", "T-Shirt")]
    [InlineData("Ultimate Paleo Protein, Vanilla (Wholesale)", "Wholesale")]
    public void Non_supplements_are_dropped(string title, string type)
    {
        var p = Product(title, type, ["Title"], (30, "Default Title", null, 25m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    // Ghost's hidden parent record: no image, duplicates the flavored listings.
    [Fact]
    public void Hidden_parent_records_are_dropped()
    {
        var p = Product("GHOST® WHEY", "", ["Title"], (70, "Default Title", null, 44.99m, true));
        p.Tags = ["base_product", "hidden", "newsite-hidden"];

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    // Titles that no word list covered in the first crawl; the options give
    // them away.
    [Theory]
    [InlineData("GHOST® FLANNEL | PLAID", "Color", "PLAID", "Size", "M")]
    [InlineData("GHOST® CANDLE x HOMESICK | ORANGE CREAM", "Color", "ORANGE CREAM", "Size", "OS")]
    [InlineData("Lifting Straps", "Color", "Black", "Title", "Default Title")]
    [InlineData("Collegiate Quarter ZIp", "Size", "XL", "Title", "Default Title")]
    [InlineData("Elbow Sleeves", "Size", "2XL", "Color", "Black")]
    [InlineData("Ambassador Welcome Box", "Size", "Medium", "Title", "Default Title")]
    public void Color_or_letter_size_options_mark_apparel_and_merch(
        string title, string option1Name, string option1, string option2Name, string option2)
    {
        var p = Product(title, "", [option1Name, option2Name], (71, option1, option2, 30m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    [Theory]
    [InlineData("Flavor", "Chocolate", "Size", "2 lb")]
    [InlineData("Flavor", "Unflavored", "Size", "Large Tub")]
    [InlineData("Size", "120 Capsules", "Title", "Default Title")]
    public void Flavor_and_package_sizes_are_not_apparel(string option1Name, string option1, string option2Name, string option2)
    {
        var p = Product("GHOST® CREATINE | UNFLAVORED", "MUSCLE BUILDER", [option1Name, option2Name],
            (72, option1, option2, 39.99m, true));

        Assert.NotEmpty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    // The same words inside real supplement titles.
    [Theory]
    [InlineData("Magnesium Glycinate 1 Bottle")]
    [InlineData("Tongkat Ali - 3 Bottle Value Pack")]
    [InlineData("Chocolate PB Cup Nut Butter (28 ounce)")]
    [InlineData("Hydrate Cherry Watermelon - TL x Diana Conforti - 40sv Tub & Signature Tumbler")]
    public void Supplements_with_merch_words_are_kept(string title)
    {
        var p = Product(title, "", ["Title"], (31, "Default Title", null, 25m, true));

        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    // Quest lists loyalty rewards at $0.01.
    [Fact]
    public void Sub_dollar_items_are_skipped()
    {
        var p = Product("Whey Protein Bar", "", ["Title"], (32, "Default Title", null, 0.01m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));
    }

    [Fact]
    public void Wholesale_drums_are_dropped_but_big_consumer_tubs_stay()
    {
        var p = Product("Creatine Monohydrate Powder", "", ["Style", "Size"],
            (33, "Powder", "1 Kilogram (2.2 lbs)", 29.96m, true),
            (34, "Powder", "25 Kilograms (55 lbs)", 499.96m, true),
            (35, "Powder", "20 lb", 104.99m, true));

        var sizes = ShopifyStoreScraper.ToScrapedProducts(p, Brand, null)
            .Select(i => ProductAttributeParser.ExtractSize(i.Name))
            .ToList();

        Assert.Equal(["1 kg", "20 lb"], sizes);
    }

    [Fact]
    public void Ingredient_store_keeps_only_its_sport_categories()
    {
        var ingredients = ShopifyStores.All.Single(s => s.BrandName == "BulkSupplements");
        var yeast = Product("Nutritional Yeast Flakes", "", ["Title"], (36, "Default Title", null, 12m, true));
        var vitamin = Product("Vitamin K2 MK7 Powder", "", ["Title"], (37, "Default Title", null, 20m, true));
        var creatine = Product("Creatine Monohydrate Powder", "", ["Title"], (38, "Default Title", null, 30m, true));

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(yeast, ingredients, null));
        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(vitamin, ingredients, null));
        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(creatine, ingredients, null));
        // Brand stores are not narrowed: their vitamins stay, and their
        // uncategorised items are stacks and bundles.
        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(vitamin, Brand, null));
        Assert.Single(ShopifyStoreScraper.ToScrapedProducts(yeast, Brand, null));
    }

    [Fact]
    public void Retailer_gear_vendor_is_dropped()
    {
        var p = Product("Contour Nylon Lifting Aid", "", ["Title"], (38, "Default Title", null, 29.99m, true));
        p.Vendor = "Bodybuilding.com Accessories";

        Assert.Empty(ShopifyStoreScraper.ToScrapedProducts(p, Retailer, "bodybuilding.com"));
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

    // DMoose files "Subscription type" (One-time / Sub) as a third option. Counted
    // as a size it doubled every row: one pre-workout became four products
    // (measured 2026-09-18 on the live catalog). The recurring variants are
    // dropped, not merged: a subscribe-and-save price is not a price a shopper
    // can pay once.
    [Fact]
    public void Subscription_variants_do_not_become_sizes()
    {
        var p = Product("Power Blast Pre-Workout", "Sports Nutrition", ["Energy", "Subscription type"],
            (1, "High Stim", "One-time", 25.00m, true),
            (2, "High Stim", "Sub", 21.25m, true),
            (3, "Non Stim", "One-time", 25.00m, true),
            (4, "Non Stim", "Sub", 21.25m, true));

        var items = ShopifyStoreScraper.ToScrapedProducts(p, Brand, null).ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(25.00m, i.Price));
        Assert.Contains(items, i => i.Name.EndsWith("High Stim"));
        Assert.Contains(items, i => i.Name.EndsWith("Non Stim"));
    }

    // A store that only sells on subscription keeps its row: an unbuyable price is
    // bad, but losing the product from the comparison is worse.
    [Fact]
    public void Subscription_only_product_is_still_published()
    {
        var p = Product("Daily Greens", "Sports Nutrition", ["Subscription type"],
            (1, "Monthly", null, 39.00m, true));

        var item = Assert.Single(ShopifyStoreScraper.ToScrapedProducts(p, Brand, null));

        Assert.Equal(39.00m, item.Price);
        Assert.Equal("Daily Greens", item.Name);
    }
}
