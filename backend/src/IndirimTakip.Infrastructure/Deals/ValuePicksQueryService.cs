using IndirimTakip.Infrastructure.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// The product list on the "Which supplement?" pages: the lowest price per kg
/// in a category (see ValuePickRanker).
///
/// DELIBERATELY SEPARATE FROM DealsQueryService: that file brought production
/// down twice in the code base this site came from, and it carries paging,
/// filters and search. This is a single read-only query; the ranking has to
/// parse the size text, so it can't be translated to SQL and runs in memory
/// (at most about a thousand rows per category).
/// </summary>
public sealed class ValuePicksQueryService(
    AppDbContext db,
    IOptions<AffiliateOptions> affiliateOptions,
    ProductImageOptions imageOptions)
{
    public async Task<ValuePicksDto> GetAsync(
        string category, string? type, int count, CancellationToken cancellationToken = default)
    {
        var staleSince = DateTimeOffset.UtcNow.Subtract(DealsQueryService.StaleThreshold);

        // The same base as the list queries: active brand, visible product
        // (global query filter), scanned in the last 48 hours, price summary set.
        var rows = await (
            from p in db.Products
            join b in db.Brands on p.BrandId equals b.Id
            where b.IsActive
                  && p.Category == category
                  && p.LatestPrice != null && p.LatestPrice > 0
                  && p.ReferencePrice30 != null && p.LowestPrice30 != null
                  && p.LatestScrapedAt != null && p.LatestScrapedAt >= staleSince
                  && p.InStock != false
                  && p.Size != null
            select new
            {
                p.Id,
                p.BrandId,
                BrandName = b.Name,
                p.Name,
                p.Size,
                p.Url,
                p.ImageUrl,
                p.LocalImagePath,
                p.Seller,
                p.InStock,
                Price = p.LatestPrice!.Value,
                ReferencePrice = p.ReferencePrice30!.Value,
                LowestPrice = p.LowestPrice30!.Value,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var ranking = ValuePickRanker.Rank(
            rows.Select(r => new ValuePickCandidate(r.Id, r.BrandId, r.BrandName, r.Name, r.Size, r.Price, r.InStock)),
            category, type, count);

        var rowsById = rows.ToDictionary(r => r.Id);
        var items = ranking.Picks
            .Select(pick =>
            {
                var r = rowsById[pick.Candidate.ProductId];
                return new ValuePickDto(
                    r.Id,
                    r.Name,
                    r.BrandName,
                    ProductImageStore.PublicUrl(r.LocalImagePath, imageOptions.PublicBaseUrl) ?? r.ImageUrl,
                    r.Size,
                    r.Seller,
                    r.Price,
                    pick.PricePerKg,
                    r.ReferencePrice,
                    // Same calculation as DealsQueryService.MapToDealDto: a product
                    // shown as "12% off" elsewhere on the site mustn't show another
                    // number here.
                    r.ReferencePrice > 0 ? Math.Round((r.ReferencePrice - r.Price) / r.ReferencePrice * 100, 1) : 0m,
                    r.Price <= r.LowestPrice && r.LowestPrice < r.ReferencePrice,
                    r.InStock,
                    AffiliateLinkBuilder.Apply(r.Url, affiliateOptions.Value));
            })
            .ToList();

        return new ValuePicksDto(items, ranking.EligibleCount);
    }
}
