namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>A Shopify store we collect prices from.</summary>
/// <param name="BrandName">Canonical brand name for a single-brand store.</param>
/// <param name="BaseUrl">Store origin, no trailing slash.</param>
/// <param name="IsRetailer">
/// True for multi-brand retailers. Their products take the brand from the
/// Shopify "vendor" field and carry the store host as the seller, which is
/// what lets the same product be compared across a brand site and a retailer.
/// </param>
/// <param name="OnlyCategories">
/// When set, only products in these categories are kept; uncategorised ones
/// go too. For stores whose catalog is mostly outside our scope.
/// </param>
/// <param name="NutritionOnPage">
/// True when the store's product pages carry the Nutrition Facts panel in a
/// readable form, so the detail backfill reads it there: as visible text
/// (Quest, measured 2026-09-14) or as a JSON object in the page (Naked
/// Nutrition, measured 2026-09-15). The other stores publish it as an image.
/// </param>
public sealed record ShopifyStore(
    string BrandName, string BaseUrl, bool IsRetailer = false, IReadOnlySet<string>? OnlyCategories = null,
    bool NutritionOnPage = false);

public static class ShopifyStores
{
    /// <summary>
    /// The sports categories, without vitamins. BulkSupplements is limited to
    /// these: 2,399 of its 3,198 products fell under "vitamins" and were mostly
    /// herbal extracts (saw palmetto, black rice extract) that no other store
    /// sells, so there is nothing to compare them against, and they would have
    /// filled 70% of that category on their own (measured 2026-09-11).
    /// </summary>
    public static readonly IReadOnlySet<string> SportCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "protein-powder", "creatine", "amino-acids", "pre-workout",
        "hydration", "mass-gainers", "fat-burners", "protein-snacks",
    };

    /// <summary>
    /// US shortlist, measured from the production server on 2026-09-11: every
    /// entry answered products.json from a datacenter IP. Brands that blocked
    /// the server (Legion, JYM) or pay affiliates only through PayPal (Thorne)
    /// were left out on purpose.
    /// </summary>
    public static readonly IReadOnlyList<ShopifyStore> All =
    [
        new("BulkSupplements", "https://www.bulksupplements.com", OnlyCategories: SportCategories),
        new("Naked Nutrition", "https://www.nakednutrition.com", NutritionOnPage: true),
        new("Transparent Labs", "https://www.transparentlabs.com"),
        new("Momentous", "https://www.livemomentous.com"),
        new("RAW Nutrition", "https://getrawnutrition.com"),
        new("Gorilla Mind", "https://gorillamind.com"),
        new("Nutricost", "https://nutricost.com"),
        new("Kaged", "https://www.kaged.com"),
        new("MuscleTech", "https://www.muscletech.com"),
        new("Promix", "https://www.promixnutrition.com"),
        new("Quest Nutrition", "https://www.questnutrition.com", NutritionOnPage: true),
        new("Clean Simple Eats", "https://cleansimpleeats.com"),
        new("Jocko Fuel", "https://www.jockofuel.com"),
        // The root storefront serves the EU catalog in EUR to our server;
        // /en-us is the US catalog in USD (measured 2026-09-11).
        new("Optimum Nutrition", "https://www.optimumnutrition.com/en-us"),
        new("Orgain", "https://www.orgain.com"),
        new("Ascent", "https://ascentprotein.com"),
        new("Ghost", "https://www.ghostlifestyle.com"),
        new("Bodybuilding.com", "https://www.bodybuilding.com", IsRetailer: true),
        // Added 2026-09-14, all on Awin (applications pending). Measured from
        // the production server: products.json 200 and a USD storefront for
        // each; domains checked, not assumed. AnimalPak's brand is "Animal"
        // (64 of its 87 products carry that vendor). "CON-CRET" matches the
        // brand row Bodybuilding.com already created, so both sources share
        // one brand page.
        new("MusclePharm", "https://musclepharm.com"),
        new("Animal", "https://www.animalpak.com"),
        new("Bare Performance Nutrition", "https://www.bareperformancenutrition.com"),
        new("Bounce Nutrition", "https://bouncenutrition.com"),
        new("Ultimate Paleo Protein", "https://ultimatepaleoprotein.com"),
        new("CON-CRET", "https://con-cret.com"),
    ];
}
