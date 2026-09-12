using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Refreshes the star average and rating count stores show on their own sites,
/// read from the product page.
///
/// Why no per-store implementation: stores with rating data publish it in the
/// product page's schema.org markup with the same fields, whatever the platform.
/// One download + one parser covers them all.
///
/// Why it wasn't added to <c>ProductDetailBackfillService</c>: that service works
/// on "fill once, never touch again" (descriptions and nutrition don't change).
/// Ratings keep changing and need regular refreshing: a different lifecycle, so a
/// separate stamp (RatingCheckedAt) and a separate service.
/// </summary>
public class ProductRatingRefreshService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<ProductRatingRefreshService> logger)
{
    // Courtesy delay between requests so store sites aren't hammered; the same
    // value as ProductDetailBackfillService.
    private static readonly TimeSpan DelayBetweenProducts = TimeSpan.FromMilliseconds(750);

    // Each run refreshes this many of the products checked longest ago. A full
    // pass over the catalog spreads across days; ratings don't change noticeably
    // from day to day, so that's enough.
    private const int MaxProductsPerRun = 80;

    // Shared client carrying a browser User-Agent: many stores (including those
    // behind Cloudflare) reject requests without one.
    public const string RatingHttpClientName = "product-rating";

    /// <summary>
    /// Refreshes products whose rating was never checked or was checked longest
    /// ago. Not limited to stores that have rating data: a store that starts
    /// collecting reviews tomorrow is picked up automatically. The stamp is written
    /// on every attempt, so products without data don't clog the queue.
    /// </summary>
    public async Task<int> RefreshAsync(int? maxProducts = null, CancellationToken cancellationToken = default)
    {
        var take = maxProducts ?? MaxProductsPerRun;

        var products = await db.Products
            .OrderBy(p => p.RatingCheckedAt ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var httpClient = httpClientFactory.CreateClient(RatingHttpClientName);
        var updated = 0;

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var html = await httpClient.GetStringAsync(product.Url, cancellationToken);
                var (value, count) = AggregateRatingParser.Parse(html);

                // A "5.0" from one or two reviews isn't an average; values below
                // this threshold aren't stored, so they don't outrank real averages.
                if (value is not null && count >= AggregateRatingParser.MinimumMeaningfulRatingCount)
                {
                    // If the rating really changed, the page content changed; the
                    // sitemap's <lastmod> should reflect that too.
                    if (product.RatingValue != value || product.RatingCount != count)
                        product.ContentUpdatedAt = DateTimeOffset.UtcNow;

                    product.RatingValue = value;
                    product.RatingCount = count;
                    updated++;
                }
                else
                {
                    // If the store removed ratings or there are no reviews, keeping
                    // the old value would be misleading.
                    product.RatingValue = null;
                    product.RatingCount = null;
                }
            }
            catch (Exception ex)
            {
                // One product page failing to load must not stop the run.
                logger.LogWarning(ex, "Could not refresh rating: {Url}", product.Url);
            }

            // The stamp is written on a failed attempt too: otherwise the same
            // unreachable product would be retried every run and clog the queue forever.
            product.RatingCheckedAt = DateTimeOffset.UtcNow;

            await Task.Delay(DelayBetweenProducts, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Rating refresh: {Checked} products checked, ratings found for {Updated}.",
            products.Count, updated);

        return updated;
    }
}
