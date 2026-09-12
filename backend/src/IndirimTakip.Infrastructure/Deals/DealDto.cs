namespace IndirimTakip.Infrastructure.Deals;

public record DealDto(
    int ProductId,
    string ProductName,
    string ProductUrl,
    string? ImageUrl,
    string? Category,
    string? Size,
    string? Flavor,
    decimal? ServingSizeGrams,
    // Servings per package as directly declared by the brand; where no package
    // weight is given, price per serving can only be calculated from this.
    int? ServingsPerPackage,
    // The real product description from the brand's own site; filled only when
    // the brand provides it, otherwise null (nothing made up).
    string? Description,
    // The real nutrition table as normalized JSON; filled only when the brand
    // provides it reliably. Used for the nutrition table on the comparison page.
    string? NutritionJson,
    decimal? ProteinPerServingGrams,
    string BrandName,
    decimal CurrentPrice,
    decimal ReferencePrice,
    decimal DiscountPercent,
    // The store's own declared (unverified) discount; separate from
    // DiscountPercent (based on our real price history) and labeled separately
    // in the UI.
    decimal? StoreOldPrice,
    decimal? StoreDiscountPercent,
    DateTimeOffset ScrapedAt,
    // Whether the current price equals the low of the same 30-day reference
    // window (the Min counterpart of ReferencePrice's Max) AND the window really
    // has a price spread (ThirtyDayLowPrice < ReferencePrice). Without the second
    // condition a product whose price never changed (Min=Max=Latest) would
    // trivially count as "at its 30-day low"; see DealsQueryService.MapToDealDto.
    bool IsAtThirtyDayLow,
    // The two fields below are filled ONLY by GetProductByIdAsync (single product
    // page); lists already hide frozen records, so there they mean nothing and
    // keep their defaults.
    //
    // The record no longer comes back in the store's scrape (see
    // StaleThreshold). The page keeps working (the price history is still
    // valuable) but must not be indexed.
    bool IsStale = false,
    // A current record with the same brand and name. Stores often don't delete a
    // product, they only change its URL; then the old URL should redirect to the
    // new record so the two pages don't compete.
    int? ReplacementProductId = null,
    // The star average and rating count on the brand's OWN site, not our
    // assessment, and labeled that way in the UI. Filled only for brands that
    // collect reviews; not comparable across brands (each uses a different
    // review system).
    decimal? RatingValue = null,
    int? RatingCount = null,
    // Could it be bought at the store in the last scrape?
    //
    // NULL = "this source doesn't report stock", not to be confused with false.
    // Where a source doesn't report it, the UI shows no badge. Out-of-stock
    // products are NOT removed from lists: they keep being scraped so the price
    // history stays unbroken, and are shown with an "Out of stock" badge.
    bool? InStock = null,
    // The store selling the product; NULL means the brand's own store.
    string? Seller = null,
    // Store URL WITH THE AFFILIATE CODE: where the "Go to store" link goes.
    //
    // Why in the DTO: the link used to go to our own /go/{id} endpoint, which
    // redirected to the store with 302. In an installed PWA that intermediate
    // redirect KILLED the back button: the new browsing context's history held
    // only the redirect chain, pressing back closed the context and the user
    // left the app (reported by a user, confirmed by measuring). A link straight
    // to the external URL has no such problem; tested in the same PWA, back
    // worked.
    //
    // /go/{id} was NOT removed: it keeps working for indexed URLs, emails and old
    // clients.
    string? StoreUrl = null,
    // If this page is a copy of ANOTHER product page, the product id of the main page.
    //
    // Stores publish the same product at several URLs (an old URL, a
    // "copy-of-..." draft, a repeat with "-1" appended), and each URL becomes a
    // separate product row here. The pages are identical, so Google treated them
    // as duplicates and picked its own canonical page.
    //
    // When set, the product page's canonical points to the main page.
    // NULL = this page is the main page (or has no copies).
    int? CanonicalProductId = null);
