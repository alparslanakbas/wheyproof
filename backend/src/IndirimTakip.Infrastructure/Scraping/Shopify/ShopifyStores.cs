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
/// A UK store's page is read as a UK table instead (see UkNutritionTable).
/// </param>
/// <param name="OnlyHandles">
/// When set, only these product handles are kept. For stores where we want a
/// few named products and a category rule would still let the rest through.
/// </param>
/// <param name="Market">
/// The edition the store belongs to; null means US. An instance registers only
/// its own market's stores and requires that market's currency from them.
/// </param>
/// <param name="OnlyInStock">
/// When set, a size with no variant in stock is skipped. For a closeout store
/// where most of the catalog is sold out: those rows would fill the listings and
/// the sitemap with pages nobody can buy from. A row that sells out stops being
/// scraped and goes stale like any other; it comes back when it is restocked
/// (the 72-hour gap rule keeps its old price from making a fake discount).
/// </param>
public sealed record ShopifyStore(
    string BrandName, string BaseUrl, bool IsRetailer = false, IReadOnlySet<string>? OnlyCategories = null,
    bool NutritionOnPage = false, IReadOnlySet<string>? OnlyHandles = null, SiteMarket? Market = null,
    bool OnlyInStock = false)
{
    public SiteMarket StoreMarket => Market ?? SiteMarket.Us;
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
        "energy-gels-drinks",
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
        // Huel left this list on 2026-10-10: it no longer publishes its products to
        // Shopify's storefront, see HuelScraper.
        // Added 2026-09-25: liquid energy, hydration, immunity and sleep shots,
        // a lion's mane capsule and a tincture. Every supplement was sold out
        // when added (products.json, the .js endpoint and the page's JSON-LD all
        // agreed); it is listed anyway for its reviews, 1,459 on the hydration
        // line alone, published in JSON-LD so the rating refresh reads them. Its
        // 13 shirts and hats are typed "Gear", already an excluded product type.
        new("Protekt", "https://protekt.com"),
        // Added 2026-09-25, on Awin (118073, application pending): two organic
        // plant proteins and two collagen shakes, one size each, $32.99. All four
        // were sold out when added, asked as a US shopper too (products.json with
        // country=US, JSON-LD OutOfStock, "sold out" on the page), and the pages
        // point to Amazon meanwhile. Listed for the Protekt reason: 74-106
        // reviews each in JSON-LD, ready the day stock returns. "Enchant" is the
        // name on the pack; "Enchant Brands" is the company.
        new("Enchant", "https://enchantbrands.com"),
        // Added 2026-10-08, all in Sovrn's network (CPC, through the network
        // fallback). Read through the real mapping code before adding: catalogs
        // downloaded via a third-party reader at night, because Shopify refuses the
        // server then and the home tunnel is what the night scrapes fall back on.
        // All twelve declare USD; hosts are where the store's own redirect lands.
        // Brands Bodybuilding.com had already created (Cellucor, BPI Sports, EHP
        // Labs, RYSE) use the database spelling, so both sources share one brand.
        //
        // A closeout store: of 4,371 variants only 192 were in stock, so it is
        // limited to stock (1,400 sold-out pages would otherwise fill the listings
        // and the sitemap) and to the sport categories (765 of its rows were
        // vitamins). 85 rows, about 40 brands; its vendor labels that differ from
        // ours are mapped in BrandNameNormalizer.
        new("Supplement Hunt", "https://supplementhunt.com", IsRetailer: true,
            OnlyCategories: SportCategories, OnlyInStock: true),
        // Sport categories only for the BulkSupplements reason: 112 own-label
        // vitamins and 62 uncategorised "Stack" bundles next to its proteins.
        new("True Nutrition", "https://truenutrition.com", OnlyCategories: SportCategories),
        new("1UP Nutrition", "https://1upnutrition.com"),
        // Sport categories only: its catalog also carries a "Test - AN offer"
        // placeholder and an e-book offer, neither of which a category lets in.
        new("Cellucor", "https://cellucor.com", OnlyCategories: SportCategories),
        new("Ultimate Nutrition", "https://ultimatenutrition.com"),
        new("Chike", "https://www.ilikechike.com"),
        new("BPI Sports", "https://bpisports.com"),
        // Formerly Designer Protein. Sport categories only: a $10 donation
        // product ("Gift for Good") sits next to the shakes.
        new("Designer Wellness", "https://designerwellness.com", OnlyCategories: SportCategories),
        new("EHP Labs", "https://ehplabs.com"),
        new("Jacked Factory", "https://jackedfactory.com"),
        // Sport categories only: of 515 catalog products, 254 are gear, 16 app
        // plans and coaching certifications ($440-540), and most of the rest are
        // multi-product "Stack" bundles with no category of ours.
        new("1st Phorm", "https://1stphorm.com", OnlyCategories: SportCategories),
        new("RYSE", "https://rysesupps.com"),

        // ---- UK SECTION (www.wheyproof.com/uk) ----------------------------
        // Scraped only by the UK instance (Market:Code=UK), priced in GBP.
        // Measured from the production server on 2026-09-19: each storefront
        // declared GBP with currency=GBP and answered products.json. All four
        // run Awin programmes (applications pending).
        new("Vivo Life", "https://www.vivolife.co.uk", Market: SiteMarket.Uk),
        // Added 2026-09-25, on Awin (115535): women's plant-based protein, three
        // products (Perform, Nourish and the two as a Starter Kit), GBP. Each has
        // a "Subscribe & Save" option; its plans are not sizes, and the price
        // taken is the "One-Off Purchase" one. Written without the
        // brand's "Ø": slugify and search don't fold it, so "LØUCO" would give
        // /brand/l-uco and a search for "louco" would miss it. The pack size and
        // nutrition are on the product page only and are entered by hand.
        new("LOUCO", "https://louco.co", Market: SiteMarket.Uk),
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
        // Its product pages print the UK table (per 100 g and per serve) as HTML, with amino acid and
        // mineral tables after it (measured 2026-10-07 on CollagenPro; 6 of 10 random records read in a dry
        // run). NOT NutritionOnPage yet: the UK backfill's first run started at 00:26 UTC on 2026-10-07,
        // when Shopify refuses the server at night, and every page went out twice, directly and through the
        // home tunnel, both 429. The runs stay anchored near that hour, so the store would be asked twice a
        // product every night and filled never, while the home tunnel is what the night scrapes of all three
        // sites fall back on. It comes back with a backfill that keeps Shopify stores to the day.
        new("Veloforte", "https://veloforte.com", Market: SiteMarket.Uk),
        // Not NutritionOnPage: its table is built from divs and its shakes are per 100 ml (2026-10-07).
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
    ];

    /// <summary>Every Shopify host we read, both markets (for signing).</summary>
    public static readonly IReadOnlySet<string> Hosts = All
        .Select(s => new Uri(s.BaseUrl).Host)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<ShopifyStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}
