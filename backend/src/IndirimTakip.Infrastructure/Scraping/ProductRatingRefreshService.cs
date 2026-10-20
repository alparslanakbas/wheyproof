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
    ILogger<ProductRatingRefreshService> logger,
    TimeSpan? delayBetweenProducts = null)
{
    // Courtesy delay between requests; the same value as ProductDetailBackfillService.
    // It was 750 ms: on 2026-10-03 Shopify rate-limited the server's address across
    // the whole platform (429 even from stores we never scrape), each time after
    // 80-page rating bursts at about one request a second. Runs are now daily
    // (RatingRefresh:IntervalHours 6 -> 24) and the requests spread out. Tests pass zero.
    private static readonly TimeSpan DelayBetweenProducts = TimeSpan.FromSeconds(5);
    private readonly TimeSpan delay = delayBetweenProducts ?? DelayBetweenProducts;

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

        // Stores that answered 429 in this run. Their remaining products are not
        // requested, only stamped like any failed attempt (see below), so a store
        // that keeps refusing doesn't pile its products up at the head of the queue.
        var refusedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var host = Uri.TryCreate(product.Url, UriKind.Absolute, out var uri) ? uri.Host : product.Url;
            if (refusedHosts.Contains(host))
            {
                product.RatingCheckedAt = DateTimeOffset.UtcNow;
                continue;
            }

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
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // The store asked us to slow down: no more of its pages in this run
                // (the same rule as ProductDetailBackfillService).
                refusedHosts.Add(host);
                logger.LogWarning("{Host} answered 429; its remaining products are skipped this run.", host);
            }
            catch (Exception ex)
            {
                // One product page failing to load must not stop the run.
                logger.LogWarning(ex, "Could not refresh rating: {Url}", product.Url);
            }

            // The stamp is written on a failed attempt too: otherwise the same
            // unreachable product would be retried every run and clog the queue forever.
            product.RatingCheckedAt = DateTimeOffset.UtcNow;

            await Task.Delay(delay, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Rating refresh: {Checked} products checked, ratings found for {Updated}.",
            products.Count, updated);

        return updated;
    }
}
