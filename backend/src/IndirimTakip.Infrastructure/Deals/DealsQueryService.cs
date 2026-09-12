using System.Globalization;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Images;
using IndirimTakip.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Infrastructure.Deals;



// The shared DealDto mapping (discount percentage etc.) of GetDealsAsync,
// GetProductByIdAsync and GetDealsByIdsAsync lives here in one place.
// IMPORTANT: this record is built in memory only AFTER materialization
// (ToListAsync/FirstOrDefaultAsync); it is NOT part of a projection EF Core has
// to translate to SQL. In its first version "Latest" was a nested record
// (PricePointRow) built directly in the query projection; later .Where() filters
// reaching into that nested record (r.Latest!.Price etc.) made EF Core fail with
// "could not be translated" and return 500 (caught in production). The fix:
// filtering/sorting runs on a flat anonymous type + the PriceHistory ENTITY
// (Latest), a pattern EF Core supports natively, and the mapping to DealRow
// happens after the list is materialized.
internal sealed record DealRow(Product Product, string BrandName, PriceHistory Latest, decimal ReferencePrice, decimal ThirtyDayLowPrice);

public partial class DealsQueryService(
    AppDbContext db,
    IOptions<AffiliateOptions> affiliateOptions,
    ProductImageOptions imageOptions)
{
    private string imageBaseUrl => imageOptions.TabanAdres;

    // When a store changes a product's SKU/URL on its site, the scraper can't
    // find the old record again and PriceHistory stops growing, but the Product
    // row stays in the database so its price history isn't lost (see
    // ScrapeIngestionService, a deliberate decision). Stores are scraped far more
    // often than this, so a product not updated for much longer (48 hours, about
    // twice the safety margin) really is gone from the store's feed. It is hidden
    // from list/statistics queries so shoppers don't see a frozen card that looks
    // actively tracked. The data is NOT deleted: the direct product link
    // (GetProductByIdAsync) and favorites (GetDealsByIdsAsync) still reach it;
    // hiding "no longer updated" from a product someone already knows or saved
    // would be misleading.
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(48);

    /// <summary>
    /// The seller filter label for "sold on the brand's own site". In the
    /// database that is Seller = NULL; a null can't travel as a filter value,
    /// so it becomes a named label. The filter list and the query share this
    /// constant so the two never drift apart.
    /// </summary>
    public const string BrandDirectSellerLabel = "Brand's own store";

    /// <summary>
    /// One label for ALL retailers.
    ///
    /// Retailers are deliberately not listed one by one: the question people
    /// ask is "am I buying from the brand or a retailer", and which retailer
    /// is already written on the product's own row. One option per retailer
    /// would grow with every source and turn a choice into an inventory scan.
    /// The frontend's brand page sends this exact text (RETAILER_LABEL).
    /// </summary>
    public const string DealerSellerLabel = "Retailers";

    // Lowest average needed to be featured. The point is showing well-liked
    // products; featuring a 3.2 average would defeat the strip's purpose.
    private const decimal MinimumRatingValue = 4.0m;

    /// <summary>
    /// Reduces search text to the SAME result as the database's `lower()`.
    ///
    /// Why: Postgres `lower('İ')` and `lower('I')` both produce "i" (measured),
    /// but .NET's invariant `ToLower()` doesn't lowercase the dotted `İ` at all,
    /// so an all-caps "VİTAMİN" search never matched "vitamin" in the database
    /// (measured on the Turkish site: 260 results vs 0). Typing in capitals is
    /// common on mobile.
    ///
    /// The dotless `ı` folds to `i` too, and the same folding is applied on the
    /// column side (see ApplyTerms). Harmless for English text, and product
    /// names from any source may carry these letters.
    /// </summary>
    internal static string NormalizeSearchText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

    private DealDto MapToDealDto(DealRow row)
    {
        var latest = row.Latest;
        var referencePrice = row.ReferencePrice;
        return new DealDto(
            row.Product.Id, row.Product.Name, row.Product.Url,
            // The local copy if there is one, otherwise the source URL. An
            // in-memory mapping, not a query; the SQL-producing part of this
            // file isn't touched.
            ProductImageStore.GenelAdres(row.Product.LocalImagePath, imageBaseUrl) ?? row.Product.ImageUrl,
            row.Product.Category, row.Product.Size, row.Product.Flavor, row.Product.ServingSizeGrams,
            row.Product.ServingsPerPackage,
            row.Product.Description,
            row.Product.NutritionJson,
            row.Product.ProteinPerServingGrams,
            row.BrandName, latest.Price, referencePrice,
            // The reference price can be zero (a product listed without a
            // price); the store discount line right below already guarded
            // against it, but this one didn't, and when sorted by price that
            // product came first and the whole list failed with division by zero.
            referencePrice > 0 ? Math.Round((referencePrice - latest.Price) / referencePrice * 100, 1) : 0m,
            latest.StoreOldPrice,
            latest.StoreOldPrice is decimal storeOld && storeOld > 0
                ? Math.Round((storeOld - latest.Price) / storeOld * 100, 1)
                : null,
            latest.ScrapedAt,
            latest.Price <= row.ThirtyDayLowPrice && row.ThirtyDayLowPrice < referencePrice,
            IsStale: false,
            ReplacementProductId: null,
            RatingValue: row.Product.RatingValue,
            RatingCount: row.Product.RatingCount,
            InStock: row.Product.InStock,
            Seller: row.Product.Seller,
            AffiliateLinkBuilder.Apply(row.Product.Url, row.BrandName, affiliateOptions.Value));
    }

    public async Task<PagedResult<DealDto>> GetDealsAsync(
        int referenceWindowDays,
        string[]? brands,
        string[]? categories,
        string[]? sellers,
        string? search,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyDiscounted,
        bool onlyStoreDiscounted,
        string? sortBy,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        // For brand PAGES: prefer the brand's own store.
        //
        // The rule: if a brand HAS products from its own store, the brand page
        // shows only those, so a retailer's copy doesn't sit next to it and list
        // the product twice. If the brand has NO direct products the filter isn't
        // applied, otherwise the pages of brands that come only from retailers
        // would be empty.
        //
        // Only the brand page sends it; the home page's brand filter does NOT use
        // it, since there the brand and seller filters must work independently.
        bool preferBrandStore = false,
        // Expanding the search term with synonyms helps when the category is
        // OPEN (a search in one wording also finds products named in another).
        // With a fixed category it backfires: in the calculator table a
        // "collagen" search returned ALL protein powders, because "collagen" is
        // one of that category's keywords. So it can be turned off there.
        bool expandSearchSynonyms = true)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        // PRECOMPUTED SUMMARY vs LIVE CALCULATION.
        //
        // This query used to run 6-8 correlated subqueries over PriceHistories
        // for EVERY one of 2,713 products; 97.7% of the request was spent here
        // (COUNT 654 ms + data query 1,437 ms, 49 ms in C#). The same values are
        // now written to the product after every scrape with one set-based query
        // (PriceSummaryRefresher).
        //
        // The summary covers a FIXED 30-day window. If the caller asks for a
        // different window (days=7, days=90) the OLD LIVE CALCULATION is used;
        // that path was kept on purpose, not deleted. So the speed-up applies
        // only to the default, in practice the only one used, and every other
        // window behaves exactly as before.
        var useSummary = referenceWindowDays == PriceSummaryRefresher.WindowDays;

        // Both branches project to the SAME shape; all the filtering and sorting
        // below doesn't know which branch was taken.
        var query = (useSummary
            ? from p in db.Products
              join b in db.Brands on p.BrandId equals b.Id
              where b.IsActive
              select new
              {
                  Product = p,
                  BrandName = b.Name,
                  LatestPrice = p.LatestPrice,
                  LatestStoreOldPrice = p.LatestStoreOldPrice,
                  LatestScrapedAt = p.LatestScrapedAt,
                  ReferencePrice = p.ReferencePrice30,
                  ThirtyDayLowPrice = p.LowestPrice30,
              }
            : from p in db.Products
              join b in db.Brands on p.BrandId equals b.Id
              where b.IsActive
              select new
              {
                  Product = p,
                  BrandName = b.Name,
                  LatestPrice = (decimal?)p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.Price).FirstOrDefault(),
                  LatestStoreOldPrice = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.StoreOldPrice).FirstOrDefault(),
                  LatestScrapedAt = (DateTimeOffset?)p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault(),
                  ReferencePrice = p.PriceHistories
                      .Where(ph => ph.ScrapedAt >= referenceSince)
                      .Max(ph => (decimal?)ph.Price),
                  ThirtyDayLowPrice = p.PriceHistories
                      .Where(ph => ph.ScrapedAt >= referenceSince)
                      .Min(ph => (decimal?)ph.Price),
              }).AsNoTracking();

        // Hide frozen/ghost products; see the comment on StaleThreshold.
        query = query.Where(r => r.LatestScrapedAt != null && r.LatestScrapedAt >= staleSince);

        if (brands is { Length: > 0 })
            query = query.Where(r => brands.Contains(r.BrandName));

        // When a single brand is targeted and the caller didn't specify a seller,
        // the brand's own store takes precedence (see preferBrandStore).
        if (preferBrandStore && brands is { Length: 1 } && sellers is null or { Length: 0 })
        {
            var targetBrand = brands[0];
            var hasOwnProducts = await db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Seller == null && p.Brand!.Name == targetBrand, cancellationToken);

            if (hasOwnProducts)
                query = query.Where(r => r.Product.Seller == null);
        }

        if (sellers is { Length: > 0 })
        {
            // "The brand's own store" is NULL in the database, so the conditions
            // are built one by one.
            var brandDirect = sellers.Contains(BrandDirectSellerLabel);
            var allRetailers = sellers.Contains(DealerSellerLabel);
            // A specific retailer name is still accepted: the UI no longer offers
            // that option, but /api/deals is public and old links
            // (sellers=<retailer host>) must keep working.
            var specificRetailers = sellers
                .Where(x => x != BrandDirectSellerLabel && x != DealerSellerLabel)
                .ToArray();
            query = query.Where(r =>
                (brandDirect && r.Product.Seller == null)
                || (allRetailers && r.Product.Seller != null)
                || (specificRetailers.Length > 0 && r.Product.Seller != null && specificRetailers.Contains(r.Product.Seller)));
        }

        if (categories is { Length: > 0 })
            query = query.Where(r => r.Product.Category != null && categories.Contains(r.Product.Category));

        // Keeps a row if AT LEAST ONE of the given terms appears in one of the
        // product's fields. Calling it several times ANDs the conditions, which
        // is exactly what word-based search relies on.
        //
        // A local function, because `query` is over an anonymous type and can't be
        // passed to a static helper.
        void ApplyTerms(string[] terms)
        {
            // The column side folds `ı` -> `i` as well; Postgres' lower() already
            // turns İ/I into "i" but leaves the dotless ı as is.
            // Categories are stored hyphenated ("protein-powder"); the hyphen
            // becomes a space so "protein powder" matches too.
            query = query.Where(r =>
                terms.Any(t => r.Product.Name.ToLower().Replace("ı", "i").Contains(t)) ||
                terms.Any(t => r.BrandName.ToLower().Replace("ı", "i").Contains(t)) ||
                (r.Product.Category != null && terms.Any(t => r.Product.Category.Replace("-", " ").ToLower().Replace("ı", "i").Contains(t))) ||
                (r.Product.Size != null && terms.Any(t => r.Product.Size.ToLower().Replace("ı", "i").Contains(t))) ||
                (r.Product.Flavor != null && terms.Any(t => r.Product.Flavor.ToLower().Replace("ı", "i").Contains(t))));
        }

        var searchTerm = NormalizeSearchText(search);
        // Also used by the relevance ordering below (before orderedQuery), so it
        // is declared outside the if block with an empty default.
        string[] searchTerms = [];
        if (!string.IsNullOrEmpty(searchTerm))
        {
            // The search term is expanded with synonyms so one wording also
            // finds products named in another (and vice versa); calls with a
            // fixed category turn this off, see expandSearchSynonyms.
            var synonyms = expandSearchSynonyms
                ? ProductAttributeParser.GetSearchSynonyms(searchTerm)
                : [];

            if (synonyms.Count > 0)
            {
                // If the typed phrase ITSELF is a known synonym group (a single
                // word or a multi-word phrase such as "pre workout"), the whole
                // phrase is searched. Splitting into words would be a REGRESSION
                // here: a phrase group maps to other words entirely, and its
                // individual words may appear in no product at all.
                searchTerms = synonyms.Append(searchTerm).Select(NormalizeSearchText).Distinct().ToArray();
                ApplyTerms(searchTerms);
            }
            else
            {
                // Not a known group: SPLIT INTO WORDS. Every word must appear in at
                // least one field (AND across words, OR across fields).
                //
                // The WHOLE text used to be searched as one piece, which broke the
                // most natural way to search: "<brand> Protein" found nothing,
                // because the brand is in the brand field and "Protein" in the
                // name, so no single FIELD contains the whole phrase. Measured on
                // the Turkish site: 41% of product names didn't contain the brand,
                // so "brand + product" searches missed them entirely.
                foreach (var word in searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var wordSynonyms = expandSearchSynonyms
                        ? ProductAttributeParser.GetSearchSynonyms(word)
                        : [];
                    var terms = wordSynonyms.Count > 0
                        ? wordSynonyms.Append(word).Select(NormalizeSearchText).Distinct().ToArray()
                        : [word];

                    ApplyTerms(terms);
                }

                // Ordering uses the whole phrase: a product whose name contains
                // the whole phrase should stay on top.
                searchTerms = [searchTerm];
            }
        }

        query = query.Where(r => r.LatestPrice != null && r.ReferencePrice != null);

        if (onlyDiscounted)
            query = query.Where(r => r.LatestPrice < r.ReferencePrice);

        if (onlyStoreDiscounted)
            query = query.Where(r => r.LatestStoreOldPrice != null && r.LatestStoreOldPrice > r.LatestPrice);

        if (minPrice is not null)
            query = query.Where(r => r.LatestPrice >= minPrice);

        if (maxPrice is not null)
            query = query.Where(r => r.LatestPrice <= maxPrice);

        var totalCount = await query.CountAsync(cancellationToken);

        // With a search, order by RELEVANCE first. A "magnesium" search returned
        // both "MAGNESIUM COMPLEX" (that ingredient only) and unrelated products
        // matched through the WHOLE synonym group of the vitamin category (omega,
        // biotin, coenzyme...); the latter could rank higher by discount and hide
        // what the shopper was actually looking for (user feedback). IMPORTANT: the
        // comparison uses all `searchTerms` (synonyms included), NOT only the raw
        // `searchTerm`; otherwise a synonym spelling could never make an exact or
        // prefix match with the product name and the product would always land
        // in the lowest group (a bug from the first version, found in production
        // after deploy). Priority: exact match > name starts with > name contains
        // (each by SHORTEST/most specific name); without a search this is a no-op.
        var relevanceOrdered = searchTerms.Length > 0
            ? query
                .OrderBy(r =>
                    searchTerms.Any(t => r.Product.Name.ToLower().Replace("ı", "i") == t) ? 0
                    : searchTerms.Any(t => r.Product.Name.ToLower().Replace("ı", "i").StartsWith(t)) ? 1
                    : searchTerms.Any(t => r.Product.Name.ToLower().Replace("ı", "i").Contains(t)) ? 2
                    : 3)
                .ThenBy(r => r.Product.Name.Length)
            : query.OrderBy(r => 0);

        // Apply the sort the shopper chose; otherwise sort by the store's declared
        // discount in the store promotions view and by our verified discount in
        // every other view. With a search this is always a secondary ordering
        // coming AFTER relevance (ThenBy).
        var orderedQuery = sortBy switch
        {
            "name_asc" => relevanceOrdered.ThenBy(r => r.Product.Name),
            "name_desc" => relevanceOrdered.ThenByDescending(r => r.Product.Name),
            "price_asc" => relevanceOrdered.ThenBy(r => r.LatestPrice).ThenBy(r => r.Product.Name),
            "price_desc" => relevanceOrdered.ThenByDescending(r => r.LatestPrice).ThenBy(r => r.Product.Name),
            // Order in which products were added: Id is an increasing identity,
            // so it gives the tracking order exactly. No separate date field is
            // needed; a product row is created only on the first scrape and
            // later scrapes update it.
            "newest" => relevanceOrdered.ThenByDescending(r => r.Product.Id),
            "oldest" => relevanceOrdered.ThenBy(r => r.Product.Id),
            _ => onlyStoreDiscounted
                ? relevanceOrdered.ThenByDescending(r => (r.LatestStoreOldPrice!.Value - r.LatestPrice!.Value) / r.LatestStoreOldPrice.Value).ThenBy(r => r.Product.Name)
                // The reference price can be 0 (a store listing a product at 0):
                // unguarded, the database threw a division by zero and the WHOLE
                // product list returned 500. Such products never count as
                // discounted, so sinking to the end of the order is right.
                : relevanceOrdered.ThenByDescending(r => r.ReferencePrice!.Value == 0m ? 0m : (r.ReferencePrice.Value - r.LatestPrice!.Value) / r.ReferencePrice.Value).ThenBy(r => r.Product.Name),
        };

        // DETERMINISTIC TIE-BREAKER. Rows with equal sort keys (same discount +
        // same name, which is exactly what one product at two sellers looks like)
        // were left to the database's order, which COULD CHANGE FROM REQUEST TO
        // REQUEST. With paging, that means a product showing on two pages or on
        // none.
        //
        // Caught while moving to the price summary: the query plan changed, the
        // order of ties changed with it, and 3 of 22 queries differed in an
        // old/new output comparison: the product SET was the same, only the order
        // of ties differed. Id is unique, so the order is now always the same.
        orderedQuery = orderedQuery.ThenBy(r => r.Product.Id);

        var pageRows = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = pageRows
            .Select(r => new DealRow(
                r.Product, r.BrandName,
                // DealRow expects a PriceHistory; the summary branch has no real
                // row, so one is built from the fields. MapToDealDto reads only
                // three of them (Price, StoreOldPrice, ScrapedAt).
                new PriceHistory
                {
                    ProductId = r.Product.Id,
                    Price = r.LatestPrice!.Value,
                    StoreOldPrice = r.LatestStoreOldPrice,
                    ScrapedAt = r.LatestScrapedAt!.Value,
                },
                r.ReferencePrice!.Value, r.ThirtyDayLowPrice!.Value))
            .Select(MapToDealDto)
            .ToList();

        return new PagedResult<DealDto>(items, totalCount, page, pageSize);
    }

    // Single product query for the product page: needed to load a product
    // independently of any list, both for a visitor arriving from a shared link
    // and during SSR (the list may not be loaded yet).
    public async Task<DealDto?> GetProductByIdAsync(
        int productId, int referenceWindowDays = 30, CancellationToken cancellationToken = default)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);

        var row = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive && p.Id == productId
            select new
            {
                Product = p,
                BrandName = b.Name,
                Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).FirstOrDefault(),
                ReferencePrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Max(ph => (decimal?)ph.Price),
                ThirtyDayLowPrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Min(ph => (decimal?)ph.Price),
            }).AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (row?.Latest is null || row.ReferencePrice is null || row.ThirtyDayLowPrice is null)
            return null;

        var dto = MapToDealDto(new DealRow(row.Product, row.BrandName, row.Latest, row.ReferencePrice.Value, row.ThirtyDayLowPrice.Value));

        // If this product is a SECONDARY record of a same brand + name group, its
        // canonical should point to the main page. Computed only in this endpoint:
        // list queries are the hot path and the canonical tag is only produced on
        // a single product page anyway.
        var duplicates = await GetDuplicateCanonicalMapAsync(cancellationToken);
        if (duplicates.TryGetValue(productId, out var mainId))
            dto = dto with { CanonicalProductId = mainId };

        // This endpoint is DELIBERATELY exempt from the lists' frozen-product
        // filter (so a directly shared link doesn't break). But that lets a record
        // the store no longer returns quietly stand as a live page: it gets no
        // internal links because lists hide it, yet it stays in the search index
        // and often competes with the current record of the same product (on the
        // Turkish site 51 of one brand's 141 products, frozen each time the brand
        // changed a product URL).
        //
        // The fix isn't deleting: keeping price history was a deliberate choice
        // from the start. Instead the page is told it isn't current and, if there
        // is one, which record replaced it.
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);
        var isStale = row.Latest.ScrapedAt < staleSince;
        if (!isStale)
            return dto;

        // A CURRENT record with the same brand + name has replaced this product
        // (the store only changed its URL).
        var replacementId = await (
            from p in db.Products
            where p.Id != productId
                && p.BrandId == row.Product.BrandId
                && p.Name == row.Product.Name
                && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).First().ScrapedAt >= staleSince
            orderby p.Id descending
            select (int?)p.Id).FirstOrDefaultAsync(cancellationToken);

        return dto with { IsStale = true, ReplacementProductId = replacementId };
    }

    // For the watchlist: loads a given set of product ids in one go, without
    // paging or sorting. The scale is small (one person's list rarely exceeds a
    // few dozen products), so it is kept simple.
    public async Task<IReadOnlyList<DealDto>> GetDealsByIdsAsync(
        IReadOnlyCollection<int> productIds, int referenceWindowDays = 30, CancellationToken cancellationToken = default)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);

        var rows = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive && productIds.Contains(p.Id)
            select new
            {
                Product = p,
                BrandName = b.Name,
                Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).FirstOrDefault(),
                ReferencePrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Max(ph => (decimal?)ph.Price),
                ThirtyDayLowPrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Min(ph => (decimal?)ph.Price),
            }).AsNoTracking().ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Latest != null && r.ReferencePrice != null && r.ThirtyDayLowPrice != null)
            .Select(r => new DealRow(r.Product, r.BrandName, r.Latest!, r.ReferencePrice!.Value, r.ThirtyDayLowPrice!.Value))
            .Select(MapToDealDto)
            .ToList();
    }

    // For the home page's "popular with shoppers" strip.
    //
    // Ordering comes from customer ratings on the brands' OWN sites, not from our
    // favorite counter. Two criteria together:
    //   1. Threshold: only products with a high average (MinimumRatingValue).
    //   2. Order: how many people rated (most rated first).
    //
    // Why the rating alone doesn't order: ratings are NOT COMPARABLE across
    // brands. Each brand uses a different review system with different conditions
    // for leaving a review; putting "5.0 from 3 reviews" above "4.89 from 2,114
    // reviews" would be misleading. The review count is a raw magnitude: how many
    // people actually tried it and said something.
    //
    // Products without a rating never enter the list: no made-up default rating
    // is produced. Only brands that collect reviews have data.
    public async Task<IReadOnlyList<DealDto>> GetPreferredProductsAsync(
        int count, int referenceWindowDays = 30, CancellationToken cancellationToken = default)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);
        var staleSince = DateTimeOffset.UtcNow - StaleThreshold;

        var rows = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive
                && p.RatingValue >= MinimumRatingValue
                && p.RatingCount != null
            select new
            {
                Product = p,
                BrandName = b.Name,
                Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).FirstOrDefault(),
                ReferencePrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Max(ph => (decimal?)ph.Price),
                ThirtyDayLowPrice = p.PriceHistories
                    .Where(ph => ph.ScrapedAt >= referenceSince)
                    .Min(ph => (decimal?)ph.Price),
            })
            // Frozen records are hidden here too: featuring a product that is no
            // longer tracked would be misleading.
            .Where(r => r.Latest != null && r.Latest.ScrapedAt >= staleSince)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // The shaping below runs in memory: the number of rated products is small
        // (a few hundred), and grouping/brand quotas would need unreadable window
        // functions in SQL. This file's established "materialize the query, then
        // shape it" pattern (see the DealRow comment) applies here too.
        var shaped = rows
            .Where(r => r.ReferencePrice != null && r.ThirtyDayLowPrice != null)
            // Size/flavor variants of one product SHARE the brand's review pool:
            // all four sizes of one product showed the same review count. Listing
            // them all filled the strip with four copies of the same product.
            // Brand + review count groups those variants reliably; the cheapest
            // of each group is taken (the most useful entry point for a visitor).
            .GroupBy(r => (r.Product.BrandId, r.Product.RatingCount))
            .Select(g => g.OrderBy(r => r.Latest!.Price).First())
            .OrderByDescending(r => r.Product.RatingCount)
            .ThenByDescending(r => r.Product.RatingValue)
            .ToList();

        // ROUND-ROBIN selection across brands. Filling in order was tried and
        // wasn't enough: review counts differ by orders of magnitude between
        // brands (thousands vs dozens), so the head of the list was one brand
        // only, and since the strip scrolls horizontally visitors only see the
        // first few cards.
        //
        // Round-robin keeps brand variety wherever the list is cut. Brands queue by
        // their most reviewed product and each round takes one product per brand.
        // It is a display rule, not a claim about the data: every card shows its
        // own rating and review count as is.
        var byBrand = shaped
            .GroupBy(r => r.Product.BrandId)
            .Select(g => g.ToList())
            .OrderByDescending(g => g[0].Product.RatingCount)
            .ToList();

        var selected = new List<DealRow>();
        for (var round = 0; selected.Count < count; round++)
        {
            var addedThisRound = false;
            foreach (var brandProducts in byBrand)
            {
                if (selected.Count >= count) break;
                if (round >= brandProducts.Count) continue;

                var r = brandProducts[round];
                selected.Add(new DealRow(r.Product, r.BrandName, r.Latest!, r.ReferencePrice!.Value, r.ThirtyDayLowPrice!.Value));
                addedThisRound = true;
            }

            // Every brand's products are used up.
            if (!addedThisRound) break;
        }

        return selected.Select(MapToDealDto).ToList();
    }

    // For brand comparison pages: average current price per category. Not static
    // or hand-written content; computed from live data on every request, so it
    // stays current as brands and products are added.
    public async Task<BrandComparisonDto?> GetBrandComparisonAsync(string brand1, string brand2, CancellationToken cancellationToken = default)
    {
        var b1 = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.IsActive && b.Name.ToLower() == brand1.ToLower(), cancellationToken);
        var b2 = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.IsActive && b.Name.ToLower() == brand2.ToLower(), cancellationToken);
        if (b1 is null || b2 is null || b1.Id == b2.Id)
            return null;

        var avg1 = await GetCategoryAveragesAsync(b1.Id, cancellationToken);
        var avg2 = await GetCategoryAveragesAsync(b2.Id, cancellationToken);

        var categories = avg1.Keys.Union(avg2.Keys)
            .OrderBy(c => c)
            .Select(c =>
            {
                avg1.TryGetValue(c, out var v1);
                avg2.TryGetValue(c, out var v2);
                return new CategoryComparisonDto(
                    c,
                    v1.count > 0 ? Math.Round(v1.avg, 2) : null, v1.count,
                    v2.count > 0 ? Math.Round(v2.avg, 2) : null, v2.count);
            })
            .ToList();

        var total1 = await db.Products.CountAsync(p => p.BrandId == b1.Id, cancellationToken);
        var total2 = await db.Products.CountAsync(p => p.BrandId == b2.Id, cancellationToken);

        return new BrandComparisonDto(b1.Name, b2.Name, total1, total2, categories);
    }

    private async Task<Dictionary<string, (decimal avg, int count)>> GetCategoryAveragesAsync(int brandId, CancellationToken cancellationToken)
    {
        var rows = await (
            from p in db.Products
            where p.BrandId == brandId && p.Category != null
            select new
            {
                Category = p.Category!,
                Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => (decimal?)ph.Price).FirstOrDefault(),
            }).AsNoTracking().ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Latest is not null)
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => (g.Average(r => r.Latest!.Value), g.Count()));
    }

    // For the protein calculator's "best value per serving" table. The
    // calculation (package weight ÷ serving = servings, then price ÷ servings)
    // needs the Size text parsed, so it can't be translated to SQL: the
    // category's products are loaded into memory and calculated, sorted and paged
    // there. It returns PAGE BY PAGE to the client: the first version embedded
    // the whole category (100 products) in the SSR output and reached 451 KB.
    // Only products whose serving size is REALLY known are listed; an assumption
    // like "30 g = 1 serving" was never made in this project.
    public async Task<PagedResult<DealDto>> GetBestValuePerServingAsync(
        string category,
        string[]? brands,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Filtering (brand/search) happens in the database; only the per-serving
        // calculation and the ordering by it remain here.
        var all = await GetDealsAsync(
            referenceWindowDays: 30,
            brands: brands,
            categories: [category],
            sellers: null,
            search: search,
            minPrice: null,
            maxPrice: null,
            onlyDiscounted: false,
            onlyStoreDiscounted: false,
            sortBy: null,
            page: 1,
            pageSize: 500,
            cancellationToken,
            // The category is already fixed; synonym expansion made the search
            // useless here (see the parameter's own comment).
            expandSearchSynonyms: false);

        var ranked = all.Items
            .Select(deal => new { Deal = deal, Servings = CalculateServings(deal) })
            // Less than one serving per package means inconsistent data.
            .Where(x => x.Servings is >= 1)
            .OrderBy(x => x.Deal.CurrentPrice / x.Servings!.Value)
            .Select(x => x.Deal)
            .ToList();

        var totalCount = ranked.Count;
        var items = ranked
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // TotalPages is computed by PagedResult itself.
        return new PagedResult<DealDto>(items, totalCount, page, pageSize);
    }

    // For brand x category pages: only pairs that REALLY have products. A page for
    // an empty combination (a category the brand doesn't sell) is exactly what
    // Google treats as thin content and doesn't index; the sitemap and internal
    // links are built from this list.
    public async Task<IReadOnlyList<BrandCategoryPairDto>> GetBrandCategoryPairsAsync(
        CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        var rows = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive
                  && p.Category != null
                  && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault() >= staleSince
            group p by new { BrandName = b.Name, Category = p.Category! } into g
            select new BrandCategoryPairDto(g.Key.BrandName, g.Key.Category, g.Count()))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.BrandName)
            .ThenByDescending(r => r.ProductCount)
            .ToList();
    }

    // For the brands directory: tracked products per brand, in ONE query.
    //
    // Summing GetBrandCategoryPairsAsync can't give this number: that list
    // requires `p.Category != null` (right there, to avoid empty pair pages).
    // When the directory summed it, uncategorised products dropped out: one brand
    // showed 85 instead of 113, 16% of the catalog was missing, and brands with
    // no categorised products showed "0 products".
    //
    // The condition here is EXACTLY THE SAME as GetBrandStatsAsync's (active brand
    // + not stale); the brand page and the directory now show the same number.
    // Change one and they drift apart, so think about both together.
    public async Task<IReadOnlyList<BrandProductCountDto>> GetBrandProductCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        var rows = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive
                  && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault() >= staleSince
            group p by b.Name into g
            select new BrandProductCountDto(g.Key, g.Count()))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(r => r.ProductCount)
            .ThenBy(r => r.BrandName)
            .ToList();
    }

    // For the calculator page's brand chips: brands with at least one product in
    // that category whose price per serving can REALLY be calculated. The general
    // /api/filters list would mislead: a brand with products in the category but
    // no serving data would show an empty table when its chip is clicked.
    public async Task<IReadOnlyList<string>> GetBestValueBrandsAsync(
        string category,
        CancellationToken cancellationToken = default)
    {
        var all = await GetBestValuePerServingAsync(category, null, null, 1, 500, cancellationToken);
        return all.Items
            .Select(d => d.BrandName)
            .Distinct()
            .OrderBy(b => b)
            .ToList();
    }

    // Servings per package. Two sources, in order of priority:
    // (1) the servings count the brand declares DIRECTLY: not derived, the most
    // reliable source;
    // (2) package weight ÷ serving size. With neither it returns null and the
    // product never enters the price-per-serving list.
    public static decimal? CalculateServings(DealDto deal)
    {
        if (deal.ServingsPerPackage is > 0)
            return deal.ServingsPerPackage.Value;

        var packageGrams = ParsePackageGrams(deal.Size);
        if (packageGrams is > 0 && deal.ServingSizeGrams is > 0)
            return packageGrams.Value / deal.ServingSizeGrams.Value;

        return null;
    }

    // Package weight in grams (lb/oz/kg/g). Counts such as capsules or
    // servings have no weight, so those products stay out of per-gram lists.
    private static decimal? ParsePackageGrams(string? size) => ProductAttributeParser.ToGrams(size);


    /// <summary>
    /// Decides which product is the "main" page within groups of the SAME BRAND +
    /// SAME NAME and returns a <c>secondary product id -> main product id</c> map.
    /// Main products and products outside any group are NOT in the map.
    ///
    /// WHY: stores publish the same product at several URLs (an old URL, a
    /// "copy-of-..." draft, a repeat with "-1" appended). Each URL becomes a
    /// separate product row here with an identical page. Google treats them as
    /// DUPLICATES and picks its own canonical: on the Turkish site Search Console's
    /// "Duplicate, Google chose different canonical than user" validation FAILED
    /// on 21 pages (measured: 67 groups, 140 products, 73 extra URLs).
    ///
    /// Rows are NOT deleted: their price history stays, and the next scrape would
    /// recreate them anyway (the source URLs are still in the store's sitemap).
    /// The only thing done is telling Google which page is the main one:
    /// secondary pages stay out of the sitemap and their canonical points to it.
    ///
    /// MAIN PAGE CHOICE: the one with the richest price history (the record
    /// tracked longest); on a tie the smallest id. The choice must give the SAME
    /// result on every call, otherwise the canonical would flip between pages.
    /// </summary>
    private async Task<Dictionary<int, int>> GetDuplicateCanonicalMapAsync(CancellationToken cancellationToken)
    {
        // A few thousand products; loading the Id/BrandId/Name triples and
        // grouping in memory is both simpler and safer than a grouped subquery EF
        // would struggle to translate.
        var all = await db.Products
            .AsNoTracking()
            .Select(p => new { p.Id, p.BrandId, p.Name })
            .ToListAsync(cancellationToken);

        var groups = all
            .GroupBy(x => (x.BrandId, x.Name))
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0)
            return [];

        var ids = groups.SelectMany(g => g.Select(x => x.Id)).ToList();

        var historyCounts = await db.PriceHistories
            .AsNoTracking()
            .Where(h => ids.Contains(h.ProductId))
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, cancellationToken);

        var map = new Dictionary<int, int>();
        foreach (var group in groups)
        {
            var main = group
                .OrderByDescending(x => historyCounts.GetValueOrDefault(x.Id))
                .ThenBy(x => x.Id)
                .First();

            foreach (var member in group.Where(x => x.Id != main.Id))
                map[member.Id] = main.Id;
        }

        return map;
    }

    // A light list for building sitemap.xml: no DealDto price calculations, only
    // the id and a lastmod to build URLs. Frozen/ghost products are excluded here
    // too (see StaleThreshold); otherwise the sitemap would keep telling Google to
    // crawl URLs no longer linked from anywhere on the site.
    public async Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        // Secondary copies of a product stay OUT of the sitemap; offering duplicate
        // pages to Google for indexing is pointless (see
        // GetDuplicateCanonicalMapAsync).
        var duplicates = await GetDuplicateCanonicalMapAsync(cancellationToken);
        var secondaryIds = duplicates.Keys.ToList();

        return await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive
                && !secondaryIds.Contains(p.Id)
                && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault() >= staleSince
            select new SitemapEntryDto(
                p.Id,
                p.Name,
                // <lastmod> is not the last SCRAPE but the moment the content last
                // really changed. The scrape measures the whole catalog every 6
                // hours, so every URL used to carry the same stamp and Google
                // ignored the signal (see Product.ContentUpdatedAt).
                p.ContentUpdatedAt
                    ?? p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault(),
                p.Description != null || p.NutritionJson != null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    // For the home page's live scraping strip: summary numbers computed live on
    // every request (the same "not static content, computed from the DB" pattern
    // as GetBrandComparisonAsync). DiscountCount/ThirtyDayLowCount use the SAME
    // reference window logic as GetDealsAsync's onlyDiscounted / IsAtThirtyDayLow,
    // only counted here in one aggregate pass.
    public async Task<HomepageStatsDto> GetHomepageStatsAsync(int referenceWindowDays = 30, CancellationToken cancellationToken = default)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        // Hide frozen/ghost products; see the comment on StaleThreshold.
        var activeProducts = (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault() >= staleSince
            select p).AsNoTracking();

        var totalProducts = await activeProducts.CountAsync(cancellationToken);

        // This used to pull Latest/ReferencePrice/ThirtyDayLowPrice of every active
        // product into .NET with ToListAsync and count in memory with
        // rows.Count(...), sending hundreds of rows over the network on every home
        // page load. statsQuery is now only an IQueryable projection (not yet SQL);
        // the two CountAsync calls add their own WHERE and let the database count,
        // so only two integers cross the network.
        var statsQuery = activeProducts.Select(p => new
        {
            Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => (decimal?)ph.Price).FirstOrDefault(),
            ReferencePrice = p.PriceHistories.Where(ph => ph.ScrapedAt >= referenceSince).Max(ph => (decimal?)ph.Price),
            ThirtyDayLowPrice = p.PriceHistories.Where(ph => ph.ScrapedAt >= referenceSince).Min(ph => (decimal?)ph.Price),
        });

        var discountCount = await statsQuery.CountAsync(
            r => r.Latest != null && r.ReferencePrice != null && r.Latest < r.ReferencePrice,
            cancellationToken);
        var thirtyDayLowCount = await statsQuery.CountAsync(
            r => r.Latest != null && r.ThirtyDayLowPrice != null && r.ReferencePrice != null
                 && r.Latest <= r.ThirtyDayLowPrice && r.ThirtyDayLowPrice < r.ReferencePrice,
            cancellationToken);
        var lastScanAt = await db.PriceHistories.MaxAsync(ph => (DateTimeOffset?)ph.ScrapedAt, cancellationToken);

        return new HomepageStatsDto(totalProducts, discountCount, thirtyDayLowCount, lastScanAt);
    }

    // For the brand page's original statistics section built on our own data: the
    // brand-filtered version of GetHomepageStatsAsync's "only CountAsync, never
    // pull rows" pattern. Competitor analysis showed brand pages to be the weakest
    // link (ours and theirs); instead of copied history/mission text about the
    // brand, it shows real data only we have (discount frequency and depth).
    // With a category, statistics come ONLY from the brand's products in that
    // category; that is how brand x category pages get their own data.
    public async Task<BrandStatsDto> GetBrandStatsAsync(
        string brandName, int referenceWindowDays = 30, string? category = null, CancellationToken cancellationToken = default)
    {
        var referenceSince = DateTimeOffset.UtcNow.AddDays(-referenceWindowDays);
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        var activeProducts = (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive && b.Name == brandName
                  && (category == null || p.Category == category)
                  && p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => ph.ScrapedAt).FirstOrDefault() >= staleSince
            select p).AsNoTracking();

        var totalProducts = await activeProducts.CountAsync(cancellationToken);

        var statsQuery = activeProducts.Select(p => new
        {
            Latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).Select(ph => (decimal?)ph.Price).FirstOrDefault(),
            ReferencePrice = p.PriceHistories.Where(ph => ph.ScrapedAt >= referenceSince).Max(ph => (decimal?)ph.Price),
            ThirtyDayLowPrice = p.PriceHistories.Where(ph => ph.ScrapedAt >= referenceSince).Min(ph => (decimal?)ph.Price),
        });

        var discountedQuery = statsQuery.Where(r => r.Latest != null && r.ReferencePrice != null && r.Latest < r.ReferencePrice);
        var discountCount = await discountedQuery.CountAsync(cancellationToken);
        var averageDiscountPercent = discountCount > 0
            ? Math.Round(
                await discountedQuery.AverageAsync(r => (double)((r.ReferencePrice!.Value - r.Latest!.Value) / r.ReferencePrice.Value * 100), cancellationToken),
                1)
            : (double?)null;

        var thirtyDayLowCount = await statsQuery.CountAsync(
            r => r.Latest != null && r.ThirtyDayLowPrice != null && r.ReferencePrice != null
                 && r.Latest <= r.ThirtyDayLowPrice && r.ThirtyDayLowPrice < r.ReferencePrice,
            cancellationToken);

        var lastScanAt = totalProducts > 0
            ? await (from p in db.Products
                     join b in db.Brands on p.BrandId equals b.Id
                     where b.Name == brandName
                     from ph in p.PriceHistories
                     select (DateTimeOffset?)ph.ScrapedAt)
                .MaxAsync(cancellationToken)
            : null;

        var averagePrice = totalProducts > 0
            ? await statsQuery.Where(r => r.Latest != null)
                .AverageAsync(r => (decimal?)r.Latest, cancellationToken)
            : null;

        return new BrandStatsDto(
            totalProducts, discountCount, thirtyDayLowCount, averageDiscountPercent, lastScanAt,
            averagePrice is null ? null : Math.Round(averagePrice.Value, 2));
    }

    // For the product review page: average/range of the current price of active
    // products in the same category. Scalar aggregation only (AverageAsync/Min/Max);
    // no product rows are pulled into .NET (the same "CountAsync, no rows" pattern
    // as GetHomepageStatsAsync).
    public async Task<CategoryPriceStatsDto?> GetCategoryPriceStatsAsync(string category, CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(StaleThreshold);

        var latestPrices = (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive && p.Category == category
            let latest = p.PriceHistories.OrderByDescending(ph => ph.ScrapedAt).FirstOrDefault()
            where latest != null && latest.ScrapedAt >= staleSince
            select latest.Price);

        var count = await latestPrices.CountAsync(cancellationToken);
        if (count == 0) return null;

        var avg = await latestPrices.AverageAsync(cancellationToken);
        var min = await latestPrices.MinAsync(cancellationToken);
        var max = await latestPrices.MaxAsync(cancellationToken);

        return new CategoryPriceStatsDto(count, Math.Round(avg, 2), min, max);
    }

    public async Task<FilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        // Distinct: two brand rows with the same name can exist, when two scrapes
        // run at once and both decide "brand missing, create it" (the permanent
        // fix would be a unique index on the name). The same brand must not show
        // up as two chips in the UI.
        var brands = await db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => b.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        var categories = await db.Products
            .AsNoTracking()
            .Where(p => p.Category != null)
            .Select(p => p.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        // The seller filter only makes sense when retailer products really exist;
        // if everything comes from brands' own stores the filter shouldn't show
        // (the UI hides the box for an empty list). Retailer NAMES aren't listed:
        // the filter has two options, see DealerSellerLabel for why.
        var hasRetailerProducts = await db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Seller != null, cancellationToken);

        List<string> sellers = hasRetailerProducts
            ? [BrandDirectSellerLabel, DealerSellerLabel]
            : [];

        return new FilterOptionsDto(brands, categories, sellers);
    }
}
