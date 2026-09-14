namespace IndirimTakip.Core.Entities;

public class Product
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>
    /// File name of the resized copy on our own server; null until downloaded.
    /// </summary>
    /// <remarks>
    /// <b>IT DOESN'T REPLACE ImageUrl, IT SITS NEXT TO IT.</b> The source URL is
    /// what lets scraping find the right record and notice a change; it is also
    /// the URL shown while no local copy exists or the download failed. So with
    /// this field empty the site works exactly as before.
    /// </remarks>
    public string? LocalImagePath { get; set; }
    public string? Category { get; set; }
    public string? Size { get; set; }
    public string? Flavor { get; set; }

    /// <summary>
    /// Whether the product is shown on the site. Can be turned off in the admin panel.
    /// </summary>
    /// <remarks>
    /// Filtering is done by a GLOBAL QUERY FILTER (see AppDbContext), not added
    /// to each query by hand: DealsQueryService alone has about 20 product
    /// queries, and missing one would leave a hidden product visible on another
    /// page or in the sitemap.
    ///
    /// The brand's <c>IsActive</c> field is SEPARATE and already works; those
    /// queries check it explicitly.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    // Could the product be bought at the store in the last scrape?
    //
    // NULL means "this source doesn't report stock", NOT "out of stock". Not
    // every source reports it; where it doesn't, the field stays empty and the
    // UI shows no badge. Three states on purpose: counting the unknown as "in
    // stock" would be made-up data.
    public bool? InStock { get; set; }

    // The store SELLING the product; separate from Brand (the manufacturer).
    // NULL = bought from the brand's own store. Filled for retailer catalogs.
    public string? Seller { get; set; }

    public int ClickCount { get; set; }

    // "Was this helpful?" vote on the product page: a simple trust signal that
    // needs no auth (same pattern as ClickCount).
    public int HelpfulYesCount { get; set; }
    public int HelpfulNoCount { get; set; }

    // Serving size (grams) from the real nutrition table. Filled only where the
    // brand's data provides it reliably.
    public decimal? ServingSizeGrams { get; set; }

    // Servings per package as DIRECTLY declared by the brand. Where a source
    // gives no package weight (Size), price per serving can't be derived any
    // other way. Elsewhere it is null and the calculation uses
    // Size ÷ ServingSizeGrams. When both exist this field wins (it is the
    // brand's own statement, not derived).
    public int? ServingsPerPackage { get; set; }

    // The real product description from the brand's own site (plain text, HTML
    // stripped). Never made up; filled only when the brand provides it. Once
    // filled it is kept across later scrapes (see ScrapeIngestionService), so
    // scrapers that don't fetch descriptions don't reset the stored value.
    public string? Description { get; set; }

    // The brand's own nutrition table as normalized key/value JSON
    // ("Protein": "24 g"). Null when the brand gives no table; nothing is
    // guessed. Shown as the nutrition table on the comparison page.
    public string? NutritionJson { get; set; }

    // Protein per serving (grams) parsed from the table above. A separate
    // column because "protein cost per serving" and sorting/filtering by it
    // can't be done inside JSON. Null when the table has no protein row.
    public decimal? ProteinPerServingGrams { get; set; }

    // When the product page was last CHECKED for nutrition, set whether or not
    // a table was found. Many products (accessories, bars, snacks) really have
    // no table; without this field the backfill would retry the same products
    // forever.
    public DateTimeOffset? NutritionCheckedAt { get; set; }

    // The store's Nutrition/Supplement Facts panel IMAGE, picked by file name
    // during the scrape (see NutritionLabelImagePicker). US brands publish the
    // label as a picture, not as text: in a 2026-09-14 survey no store had a
    // nutrition table in its product description, while most named the label
    // image "..._SFP_..." or "...-nutrition-facts...".
    public string? NutritionLabelImageUrl { get; set; }

    // The label image URL that was last READ. A label is read once per image
    // URL: nutrition panels rarely change, and when a brand does change one the
    // file (and so the URL) changes too, which queues the new image by itself.
    public string? NutritionLabelReadUrl { get; set; }

    // Outcome of the last label read, for review: "accepted: ..." or
    // "rejected: ...". A rejected read writes NO nutrition data.
    public string? NutritionLabelStatus { get; set; }

    // When the page's CONTENT last really changed; the sitemap's <lastmod> uses it.
    //
    // Why a separate field: the last scrape time (PriceHistory.ScrapedAt) was used
    // first, but the scrape measures the WHOLE catalog every 6 hours, so nearly
    // every sitemap URL carried the same stamp. Google only trusts lastmod when
    // it is consistently accurate; on a site claiming "all my pages changed at
    // once" it ignores the signal entirely, leaving no hint about which page is
    // worth crawling.
    //
    // It is updated ONLY on a real change: the price actually changed, or the
    // name/category/nutrition/description/rating changed. Measuring the same
    // price again is NOT a change.
    public DateTimeOffset? ContentUpdatedAt { get; set; }

    // The star average shown on the brand's OWN site and how many people rated.
    // Not our assessment but the brand's customers', and labeled that way in
    // the UI.
    //
    // NOT comparable across brands: each brand uses a different review system
    // with different conditions for leaving a review. So ranking uses the
    // rating together with the review count, never the rating alone. Brands
    // that don't collect reviews stay null.
    public decimal? RatingValue { get; set; }
    public int? RatingCount { get; set; }

    // Ratings CHANGE over time (unlike descriptions and nutrition), so this is
    // not a "fetched once, done" stamp but a refresh-order field: the products
    // checked longest ago are refreshed first.
    public DateTimeOffset? RatingCheckedAt { get; set; }

    public ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();

    // ---- Price summary (precomputed) ----------------------------------------
    //
    // These five fields are DERIVED from PriceHistories, not source data; price
    // history remains the single source of truth. They are recomputed after
    // every scrape with one set-based query (PriceSummaryRefresher).
    //
    // WHY: 97.7% of /api/deals time was spent in PostgreSQL, because the query
    // ran 6-8 correlated subqueries over PriceHistories for EVERY product (last
    // price, 30-day high, low, and the same subqueries repeated for the discount
    // percentage). Measured: COUNT 654 ms + data query 1,437 ms.
    //
    // THE WINDOW IS FIXED AT 30 DAYS. If the `days` parameter differs from 30
    // the query falls back to the old live calculation; that path was kept on
    // purpose.

    /// <summary>Most recently scraped price.</summary>
    public decimal? LatestPrice { get; set; }

    /// <summary>Old price the store declared in the latest scrape.</summary>
    public decimal? LatestStoreOldPrice { get; set; }

    /// <summary>Time of the latest price point. The stale product filter uses it.</summary>
    public DateTimeOffset? LatestScrapedAt { get; set; }

    /// <summary>HIGHEST price of the last 30 days; the "verified discount" is calculated from it.</summary>
    public decimal? ReferencePrice30 { get; set; }

    /// <summary>LOWEST price of the last 30 days; the "30-day low" badge.</summary>
    public decimal? LowestPrice30 { get; set; }

    /// <summary>
    /// When the summary was last computed. Updated after a scrape; if it is very
    /// stale the query can take the safe side and fall back to the live calculation.
    /// </summary>
    public DateTimeOffset? PriceSummaryUpdatedAt { get; set; }
}
