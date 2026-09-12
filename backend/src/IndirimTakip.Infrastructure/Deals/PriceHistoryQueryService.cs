using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Deals;

public class PriceHistoryQueryService(AppDbContext db)
{
    public async Task<PriceHistoryDto?> GetPriceHistoryAsync(
        int productId,
        int days,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
            return null;

        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var points = await db.PriceHistories
            .Where(ph => ph.ProductId == productId && ph.ScrapedAt >= since)
            .OrderBy(ph => ph.ScrapedAt)
            .Select(ph => new PricePointDto(ph.Price, ph.ScrapedAt))
            .ToListAsync(cancellationToken);

        if (points.Count == 0)
            return new PriceHistoryDto(product.Id, product.Name, product.Brand!.Name, [], 0, 0, 0);

        return new PriceHistoryDto(
            product.Id,
            product.Name,
            product.Brand!.Name,
            points,
            CurrentPrice: points[^1].Price,
            MinPrice: points.Min(p => p.Price),
            MaxPrice: points.Max(p => p.Price));
    }

    // For the mini sparklines on product cards (postponed at first because of the
    // N+1 request risk). Returns every price point for one page (24 cards) in a
    // single request. It uses an anonymous type + in-memory grouping, to avoid
    // repeating the earlier DealsQueryService bug where a named record couldn't
    // be translated by EF Core.
    public async Task<IReadOnlyList<ProductSparklineDto>> GetSparklinesAsync(
        IReadOnlyList<int> productIds,
        int days,
        CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
            return [];

        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var rows = await db.PriceHistories
            .Where(ph => productIds.Contains(ph.ProductId) && ph.ScrapedAt >= since)
            .OrderBy(ph => ph.ScrapedAt)
            .Select(ph => new { ph.ProductId, ph.Price, ph.ScrapedAt })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ProductId)
            .Select(g => new ProductSparklineDto(
                g.Key,
                g.Select(r => new PricePointDto(r.Price, r.ScrapedAt)).ToList()))
            .ToList();
    }
}
