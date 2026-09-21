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
/// <param name="OnlyHandles">
/// When set, only these product handles are kept. For stores where we want a
/// few named products and a category rule would still let the rest through.
/// </param>
/// <param name="Market">
/// The edition the store belongs to; null means US. An instance registers only
/// its own market's stores and requires that market's currency from them.
/// </param>
/// <param name="CatalogUrl">
/// Where the catalog is READ from, when that differs from where shoppers are
/// SENT. Huel's public site sits behind a geo redirect that sends our Frankfurt
/// server to its German store, while the Shopify backend behind it answers the
/// US catalog from anywhere. Product links still use <see cref="BaseUrl"/>,
/// the address a US visitor can open.
/// </param>
public sealed record ShopifyStore(
    string BrandName, string BaseUrl, bool IsRetailer = false, IReadOnlySet<string>? OnlyCategories = null,
    bool NutritionOnPage = false, IReadOnlySet<string>? OnlyHandles = null, SiteMarket? Market = null,
    string? CatalogUrl = null)
{
    public SiteMarket StoreMarket => Market ?? SiteMarket.Us;

    /// <summary>The address the catalog and its currency are read from.</summary>
    public string CatalogBase => CatalogUrl ?? BaseUrl;
}

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
        // Added 2026-09-17: a 12-product store, taken for two products only.
        // The rest is off-scope (diet drops, "female enhancement", a skin
        // serum, detox blends) or a checkout add-on ("Protect", 100 price
        // tiers). A category rule would still let the fat burner and detox
        // products in, so the two handles are listed by name.
        new("33 Nutrition", "https://33nutrition.com",
            OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "complete-multivitamin", "bcaa-recovery" }),
        // Added 2026-09-18, on Awin (application pending). A 137-product store that
        // is mostly apparel and gym equipment: measured through the live filters,
        // the sport categories keep the supplement rows (whey isolate, creatine,
        // EAA, pre-workout, fat burner) and drop the rest. Vitamins are excluded for
        // the BulkSupplements reason: its wellness range (sea moss, fadogia,
        // berberine) is herbal extracts no other store sells, so there is nothing to
        // compare them against.
        new("DMoose", "https://www.dmoose.com", OnlyCategories: SportCategories),
        // Added 2026-09-19, on Awin (application pending; its US programme
        // converts at 10.4% against 3.4% for the UK one). vivolife.com is a
        // separate US Shopify store in USD (measured from the server); the UK
        // section scrapes vivolife.co.uk in GBP below. 32 rows through the real
        // scraper, no accessories. "Perform Protein UK" is its own listing next
        // to "Perform Protein" at a different price, a real product on this store.
        new("Vivo Life", "https://www.vivolife.com"),
        // Added 2026-09-19, on Awin (application pending): functional mushroom
        // gummies and elixirs. tryauri.com is Shopify in USD, but most of its 31
        // catalog rows are not products a shopper can buy on their own: "FREE"
        // subscription gifts priced at $20 (a plush toy, a tote bag, a tumbler),
        // checkout add-ons (bonus bags, shipping protection, a VIP upgrade) and
        // three campaign copies of Daily Gummies at other prices. The twelve real
        // products are listed by handle, as with 33 Nutrition.
        new("Auri Nutrition", "https://www.tryauri.com",
            OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mushroom-gummies-10plex", "super-mushroom-focus-gummies", "super-mushroom-chill-gummies",
                "super-mushroom-hsn-gummies-hair-skin-nails", "kids-daily-gummies", "super-mushroom-reishi-elixir",
                "super-mushroom-lion-s-mane-elixir", "super-mushroom-cordyceps-elixir", "super-mushroom-chaga-elixir",
                "pre-probiotic-elixir", "performance-power-bundle", "mind-focus-bundle",
            }),
        // Added 2026-09-21. Its Awin US programme REJECTED the first application,
        // most likely because the reviewer found no Huel products here: the public
        // site 307s our Frankfurt server to de.huel.com (a Vercel geo redirect keyed
        // on a huel_user_country_iso cookie). The Shopify backend behind it,
        // huelamerica.myshopify.com, answers the US catalog from anywhere and its
        // meta.json declares USD, so the catalog is read there while shoppers are
        // sent to huel.com.
        // LISTED BY HANDLE because half the backend has no public page: every one
        // of the 48 rows the scraper produced was opened as a US visitor, and 22
        // answered 404 (single units, multi-packs and bundle parts the site sells
        // only through its cart builders). Linking them would send shoppers, and
        // affiliate clicks, to a dead page. The handles below answered 200. The
        // store's sitemap was NOT used as the list: it omits five of these pages.
        new("Huel", "https://huel.com", CatalogUrl: "https://huelamerica.myshopify.com",
            OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "huel", "huel-black-edition", "black-edition-10-meals", "huel-essential",
                "huel-complete-protein", "huel-daily-superblend", "huel-daily-greens",
                "huel-daily-greens-ready-to-drink", "huel-ready-to-drink",
                "huel-black-edition-ready-to-drink", "huel-bar", "huel-energy-plus",
                "huel-instant-meal-pots", "hot-and-savoury-meal-packs", "bestseller-bundle",
                "huel-bestseller-bundle", "breakfast-lunch-bundle", "discovery-bundle-v2",
                "fiber-boost-bundle", "glp1-companion-pack", "high-protein-starter-kit",
                "huel-high-protein-bundle",
            }),

        // ---- UK SECTION (www.wheyproof.com/uk) ----------------------------
        // Scraped only by the UK instance (Market:Code=UK), priced in GBP.
        // Measured from the production server on 2026-09-19: each storefront
        // declared GBP with currency=GBP and answered products.json. All four
        // run Awin programmes (applications pending).
        new("Vivo Life", "https://www.vivolife.co.uk", Market: SiteMarket.Uk),
        // Added 2026-09-20, on Awin (application pending). A UK protein brand
        // since 1995; 88 catalog rows become 78 after the shared filters drop
        // its clothing, water bottles and shakers (measured through the real
        // scraper, no leaks). Two quirks are the store's own listings and are
        // published as they are: one bar appears under two product pages, and
        // "Whey Protein Matcha - 10% OFF" is a second listing priced above the
        // plain one.
        new("MaxiNutrition", "https://www.maxinutrition.com", Market: SiteMarket.Uk),
        // Added 2026-09-20, on Awin (application pending). Endurance nutrition:
        // chia energy gels, carb drinks and meal replacements next to protein
        // powders, so almost half of its 27 rows carry no category of ours and
        // that is correct, they are real products with no matching bucket.
        // Its Chia Soft Flask leaked through the accessory filter and is what
        // added "flask" to it; the gels-plus-flask starter pack still comes in,
        // because the filter reads the product title and only the variant label
        // mentions the flask.
        new("33Fuel", "https://www.33fuel.com", Market: SiteMarket.Uk),
        // Added 2026-09-20, on Awin (application pending). By far our largest
        // single-brand store at 438 rows, and the reason is worth knowing before
        // anyone reads it as a bug: this store gives every FLAVOUR its own
        // product page, so flavour collapsing never applies and each flavour is
        // multiplied by its sizes (whey alone is 110 rows). The names are all
        // distinct and nothing repeats, measured through the real scraper.
        // No category limit: its 37 uncategorised rows are mostly meal
        // replacements, real products with no bucket of ours.
        // Its "Fizzy Bubblegum Bottles" products are a sweet flavour, not
        // containers, and the accessory filter reads only "water bottle", so
        // they come in correctly. One row, "Free PER4M Energy 10 Serv", is
        // priced GBP 6.99 by the store despite its name; we publish the price
        // the store charges.
        new("PER4M", "https://per4mbetter.com", Market: SiteMarket.Uk),
        // Added 2026-09-20, on Awin (application pending). Natural endurance
        // nutrition: energy bars, chews and gels next to recovery proteins and
        // electrolytes, 152 rows, each family sold in 3, 12 and 24 packs. Like
        // 33Fuel it leaves a third of its rows without a category of ours,
        // because chews and gels have no bucket here and inventing one would
        // put them where no shopper looks for them.
        // Its Ultimate Hydration Bundle ships two bottles with three electrolyte
        // blends and comes in on purpose, the way supplement bundles with a
        // gifted shaker do; it arrives as two rows at the same price that differ
        // only in bottle colour.
        new("Veloforte", "https://veloforte.com", Market: SiteMarket.Uk),
        new("Grenade", "https://www.grenade.com", Market: SiteMarket.Uk),
        // Same brand as the US entry above, different storefront: /en-gb is the
        // UK catalog in GBP. Each instance registers only its own market's row.
        new("Optimum Nutrition", "https://www.optimumnutrition.com/en-gb", Market: SiteMarket.Uk),
        // A multi-brand UK retailer (its own label plus Warrior, Sports Fuel,
        // The Bulk Protein Company...). The www host redirects to the bare one,
        // so the bare host is the base and the seller label. Limited to the sport
        // categories for the BulkSupplements reason: of its 644 size rows, 241
        // had no category (bundles, "Detox Bundle", cream of rice, turmeric) and
        // 93 were vitamins, mostly its own label with nothing to compare against
        // (measured through the real scraper, 2026-09-19).
        new("Bodybuilding Warehouse", "https://bodybuildingwarehouse.co.uk", IsRetailer: true,
            OnlyCategories: SportCategories, Market: SiteMarket.Uk),
        // Added 2026-09-21, on Awin (application pending), the same way as the US
        // entry above: uk.huel.com 307s our Frankfurt server to de.huel.com, while
        // its Shopify backend, hibble.myshopify.com, answers the UK catalog from
        // anywhere and declares GBP in meta.json. 86 product pages opened as a GB
        // visitor, 27 answered 200 and are listed here (32 rows); the other 59 are
        // single units and bundle parts sold only through the cart builder.
        new("Huel", "https://uk.huel.com", Market: SiteMarket.Uk, CatalogUrl: "https://hibble.myshopify.com",
            OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "huel", "huel-black-edition", "black-edition-10-meals", "black-edition-10-meals-banana",
                "black-edition-bulk-save", "huel-essential", "huel-complete-protein", "huel-professional",
                "huel-gluten-free", "huel-diet-powder", "huel-daily-greens", "huel-daily-greens-ready-to-drink",
                "huel-daily-a-z-vitamins", "huel-ready-to-drink", "huel-black-edition-ready-to-drink",
                "huel-lite-ready-to-drink", "huel-bar", "hot-and-savoury-meal-packs",
                "hot-and-savoury-black-edition-ramen", "hot-and-savoury-lite-ramen", "huel-bestseller-bundle",
                "huel-taster-bundle", "breakfast-lunch-bundle", "daily-wellness-set", "high-protein-starter-kit",
                "huel-high-protein-bundle", "light-lean-bundle",
            }),
    ];

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<ShopifyStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}
