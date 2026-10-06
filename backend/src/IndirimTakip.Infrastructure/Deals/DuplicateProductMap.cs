using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Deals;

/// <remarks>
/// The product page's canonical (DealsQueryService) and the sitemap
/// (CatalogStatsQueryService) MUST use the same map: if they drifted apart, a
/// page missing from the sitemap would claim to be the main page, or the other
/// way round. So this is the one copy both services share.
/// </remarks>
internal static class DuplicateProductMap
{
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
    public static async Task<Dictionary<int, int>> BuildAsync(AppDbContext db, CancellationToken cancellationToken)
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

    /// <summary>
    /// What <see cref="BuildAsync"/> would answer for one product: the main record's id when the
    /// product is a secondary record of a same brand + name group, otherwise null. Same rule (most
    /// price history, then the lowest id), but only this product's group is read.
    /// </summary>
    /// <remarks>
    /// The product endpoint (<c>GetProductByIdAsync</c>) used to build the whole map on every
    /// request: every product, plus a count of all history rows of every duplicate-group product
    /// (2026-10-06). Google requests each product page uncached, so every crawl paid for it.
    /// </remarks>
    public static async Task<int?> MainIdAsync(
        AppDbContext db, int productId, int brandId, string name, CancellationToken cancellationToken)
    {
        var group = await db.Products
            .AsNoTracking()
            .Where(p => p.BrandId == brandId && p.Name == name)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (group.Count < 2)
            return null;

        var historyCounts = await db.PriceHistories
            .AsNoTracking()
            .Where(h => group.Contains(h.ProductId))
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, cancellationToken);

        var main = group
            .OrderByDescending(id => historyCounts.GetValueOrDefault(id))
            .ThenBy(id => id)
            .First();

        return main == productId ? null : main;
    }
}
