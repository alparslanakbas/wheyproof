using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// Catalog queries that DON'T use the list query's filter/sort/paging
/// pipeline: sitemap, homepage and brand stats, category price summary, filter
/// options.
/// </summary>
/// <remarks>
/// <b>WHY SEPARATE (security/architecture review, 2026-09-26).</b>
/// DealsQueryService had grown past 1,000 lines and 13 public methods; the
/// same class took the Turkish site down twice, and methods that never enter the
/// list pipeline were only noise there. These share just the staleness
/// threshold and the seller labels with it. A PURE MOVE: the bodies are
/// unchanged; endpoint JSON output and route metadata were compared before and
/// after.
/// </remarks>
public sealed class CatalogStatsQueryService(AppDbContext db)
{
    // A light list for building sitemap.xml: no DealDto price calculations, only
    // the id and a lastmod to build URLs. Frozen/ghost products are excluded here
    // too (see StaleThreshold); otherwise the sitemap would keep telling Google to
    // crawl URLs no longer linked from anywhere on the site.
    public async Task<IReadOnlyList<SitemapEntryDto>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(DealsQueryService.StaleThreshold);

        // Secondary copies of a product stay OUT of the sitemap; offering duplicate
        // pages to Google for indexing is pointless (see
        // DuplicateProductMap).
        var duplicates = await DuplicateProductMap.BuildAsync(db, cancellationToken);
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
        var staleSince = DateTimeOffset.UtcNow.Subtract(DealsQueryService.StaleThreshold);

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
        var staleSince = DateTimeOffset.UtcNow.Subtract(DealsQueryService.StaleThreshold);

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
        var staleSince = DateTimeOffset.UtcNow.Subtract(DealsQueryService.StaleThreshold);

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
            ? [DealsQueryService.BrandDirectSellerLabel, DealsQueryService.DealerSellerLabel]
            : [];

        return new FilterOptionsDto(brands, categories, sellers);
    }
}
