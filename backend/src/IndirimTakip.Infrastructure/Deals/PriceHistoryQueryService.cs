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
                RunEndpoints(g.Select(r => new PricePointDto(r.Price, r.ScrapedAt)).ToList())))
            .ToList();
    }

    /// <summary>
    /// Of consecutive points with the same price, keeps only the first and last
    /// of each run; the drawing doesn't change.
    /// </summary>
    /// <remarks>
    /// The card chart places points by time and joins them with straight lines
    /// (frontend <c>spark-chart.ts</c>). The inner points of an equal-price run
    /// lie on the flat line between the run's first and last point, so dropping
    /// them leaves the line and the area under it exactly the same. Prices are
    /// scraped several times a day and change rarely, so ~140 points over 30
    /// days come down to a few for most products.
    ///
    /// Why (2026-10-06): a product page renders the main list too, so the 24
    /// cards' sparklines rode along in the page's embedded transfer state (US
    /// product page ~250 KB of HTML, ~180 KB of it transfer state). One point a
    /// day was not chosen: it erases a dip within a day and changes the drawing.
    /// </remarks>
    internal static List<PricePointDto> RunEndpoints(List<PricePointDto> points)
    {
        var result = new List<PricePointDto>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var sameAsPrevious = i > 0 && points[i - 1].Price == points[i].Price;
            var sameAsNext = i < points.Count - 1 && points[i + 1].Price == points[i].Price;
            if (!(sameAsPrevious && sameAsNext))
                result.Add(points[i]);
        }
        return result;
    }
}
