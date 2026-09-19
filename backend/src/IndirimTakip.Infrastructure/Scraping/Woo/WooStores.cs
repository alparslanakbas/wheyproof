using IndirimTakip.Infrastructure.Scraping.Shopify;

namespace IndirimTakip.Infrastructure.Scraping.Woo;

public static class WooStores
{
    /// <summary>
    /// WooCommerce stores, measured from the production server on 2026-09-18:
    /// the Store API answered with a USD catalog from a datacenter IP.
    /// </summary>
    public static readonly IReadOnlyList<WooStore> All =
    [
        // Form is a UK brand with a US storefront, and the market is in the PATH:
        // the root answers our server in GBP, "/us" answers in USD (both measured).
        // The US catalog is 15 products; the sport categories keep the proteins,
        // creatine, protein bar and pre-workout. Its vitamin and nootropic range
        // (Multi, Radiant, Edge, ZZZZs) stays out for the BulkSupplements reason:
        // nothing else in the catalog compares against it.
        new("Form", "https://formnutrition.com/us", OnlyCategories: ShopifyStores.SportCategories),

        // The same brand's home storefront, for the UK section: the root
        // answers GBP and "/us" answers USD, measured again from the server on
        // 2026-09-20 before this was added. Its Awin programme is UK-only, so
        // until now we listed the brand where we could not earn on it.
        // The category limit is the US entry's, for the same reason, and it
        // also drops a test row the store publishes at GBP 1 ("BMTESTPRODUCT"):
        // 14 catalog rows become 8.
        new("Form", "https://formnutrition.com", OnlyCategories: ShopifyStores.SportCategories,
            Market: SiteMarket.Uk),
    ];

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<WooStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}
