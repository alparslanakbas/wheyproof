namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>A Shopify store we collect prices from.</summary>
/// <param name="BrandName">Canonical brand name for a single-brand store.</param>
/// <param name="BaseUrl">Store origin, no trailing slash.</param>
/// <param name="IsRetailer">
/// True for multi-brand retailers. Their products take the brand from the
/// Shopify "vendor" field and carry the store host as the seller, which is
/// what lets the same product be compared across a brand site and a retailer.
/// </param>
/// <param name="RequireCategory">
/// True for stores whose catalog is mostly raw ingredients outside our scope.
/// Only products that fall into one of our categories are kept.
/// </param>
public sealed record ShopifyStore(string BrandName, string BaseUrl, bool IsRetailer = false, bool RequireCategory = false);

public static class ShopifyStores
{
    /// <summary>
    /// US shortlist, measured from the production server on 2026-09-11: every
    /// entry answered products.json from a datacenter IP. Brands that blocked
    /// the server (Legion, JYM) or pay affiliates only through PayPal (Thorne)
    /// were left out on purpose.
    /// </summary>
    public static readonly IReadOnlyList<ShopifyStore> All =
    [
        new("BulkSupplements", "https://www.bulksupplements.com", RequireCategory: true),
        new("Naked Nutrition", "https://www.nakednutrition.com"),
        new("Transparent Labs", "https://www.transparentlabs.com"),
        new("Momentous", "https://www.livemomentous.com"),
        new("RAW Nutrition", "https://getrawnutrition.com"),
        new("Gorilla Mind", "https://gorillamind.com"),
        new("Nutricost", "https://nutricost.com"),
        new("Kaged", "https://www.kaged.com"),
        new("MuscleTech", "https://www.muscletech.com"),
        new("Promix", "https://www.promixnutrition.com"),
        new("Quest Nutrition", "https://www.questnutrition.com"),
        new("Clean Simple Eats", "https://cleansimpleeats.com"),
        new("Jocko Fuel", "https://www.jockofuel.com"),
        // The root storefront serves the EU catalog in EUR to our server;
        // /en-us is the US catalog in USD (measured 2026-09-11).
        new("Optimum Nutrition", "https://www.optimumnutrition.com/en-us"),
        new("Orgain", "https://www.orgain.com"),
        new("Ascent", "https://ascentprotein.com"),
        new("Ghost", "https://www.ghostlifestyle.com"),
        new("Bodybuilding.com", "https://www.bodybuilding.com", IsRetailer: true),
    ];
}
