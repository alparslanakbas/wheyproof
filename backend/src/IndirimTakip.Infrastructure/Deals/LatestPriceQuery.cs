using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// The newest price point of the given products, one per product.
/// </summary>
/// <remarks>
/// <c>p.PriceHistories.OrderByDescending(...).FirstOrDefault()</c> inside a projection becomes, in EF, a
/// <c>ROW_NUMBER() OVER (PARTITION BY "ProductId" ...)</c> window over the WHOLE <c>PriceHistories</c> table,
/// joined to the wanted products afterwards (2026-10-06 on ProteinAvcisi: 0.4-0.9 s for one product, 0.75 s
/// for a two-product favorites list). Here the filter comes before the window: only the wanted products'
/// rows are read.
/// </remarks>
internal static class LatestPriceQuery
{
    public static Task<Dictionary<int, PriceHistory>> ForProductsAsync(
        AppDbContext db, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        db.PriceHistories
            .AsNoTracking()
            .Where(ph => productIds.Contains(ph.ProductId))
            .GroupBy(ph => ph.ProductId)
            .Select(g => g.OrderByDescending(ph => ph.ScrapedAt).First())
            .ToDictionaryAsync(ph => ph.ProductId, cancellationToken);
}
