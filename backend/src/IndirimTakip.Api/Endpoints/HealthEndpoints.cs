using IndirimTakip.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Api.Endpoints;

/// <summary>
/// Source freshness health endpoint, for external monitoring.
///
/// <b>WHY IT EXISTS.</b> Scraping stopping SILENTLY is the sneakiest failure in
/// this project: the site keeps working, pages open, no error shows anywhere.
/// The problem only surfaces 48 hours later, when products cross the staleness
/// threshold and drop off the lists, and only if someone looks at the site.
///
/// The same silent failure is possible for every source: a store changes its
/// markup, blocks our IP, or a scraper's pattern stops matching.
///
/// The endpoint is PUBLIC and unauthenticated so a monitor such as UptimeRobot
/// can read it: its content is already public (store names and last scrape
/// time, visible on the site). It doesn't write anything.
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>
    /// How long a source may go without a scrape before it counts as stale.
    ///
    /// 26 hours, not 6: not every source is scraped every 6 hours. The least
    /// frequent sources run once a day, so the threshold is 24 hours plus a
    /// 2 hour margin. That stays BELOW the 48 hour staleness threshold the lists
    /// use, so we hear about it before products drop off the site, with about
    /// 22 hours left to fix it.
    ///
    /// Overridable through configuration (<c>Health:StaleHours</c>), and not only
    /// for flexibility: this is an ALARM, and an alarm never seen firing is worse
    /// than no alarm. Lowering the threshold temporarily is the only way to prove
    /// in production that the 503 path really works, and the threshold must be
    /// adjustable without a deploy once the alarm is set up.
    /// </summary>
    private const int DefaultStaleHours = 26;

    /// <summary>
    /// Older than this counts as a "retired source", not a failure.
    ///
    /// The products of a source we disable stay in the database (so price history
    /// isn't lost, a deliberate decision). Those rows would exceed the threshold
    /// forever and the endpoint would stay red PERMANENTLY, and an alarm that is
    /// always red becomes an alarm nobody looks at. A source not updated for a
    /// month isn't news; it is listed in the body for information but does NOT
    /// produce a 503.
    /// </summary>
    private const int RetiredAfterDays = 30;

    public static void MapHealthEndpoints(this WebApplication app)
    {
        var staleHours = app.Configuration.GetValue<int?>("Health:StaleHours")
                         ?? DefaultStaleHours;

        // GET *and* HEAD, both REQUIRED.
        //
        // UptimeRobot (and many monitoring tools) send HEAD by default for HTTP
        // monitors. ASP.NET Core doesn't match a HEAD request to MapGet and returns
        // 405; the monitor counts that as "down", and with no body even the
        // response time can't be measured. That happened on the Turkish site: the
        // endpoint returned 200 to GET while the monitor stayed red, and the
        // monitor was blamed. HEAD sends no body but the same status code, so the
        // 200/503 distinction, which is all monitoring needs, is kept.
        app.MapMethods("/api/health/sources", ["GET", "HEAD"], async (AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var staleBefore = now.AddHours(-staleHours);
            var retiredBefore = now.AddDays(-RetiredAfterDays);

            // Source = who brings the product in: the seller for retailer
            // products, the brand itself for products from the brand's own
            // store. Grouping by seller alone wouldn't do: the scrapers of the
            // many brands not coming from retailers can break one by one, and
            // they'd all fall into a single "brand's own store" bucket where a
            // failure stays invisible as long as one of them works.
            // IgnoreQueryFilters: hidden products are still SCRAPED (ingestion
            // bypasses the filter). This endpoint answers "is scraping still
            // running", so it must reflect reality; otherwise a source whose
            // products were all hidden would drop off the list and could never
            // raise the alarm.
            var sources = await db.Products
                .IgnoreQueryFilters()
                .Where(p => p.LatestScrapedAt != null)
                .GroupBy(p => p.Seller ?? p.Brand!.Name)
                .Select(g => new
                {
                    Source = g.Key,
                    LastScraped = g.Max(p => p.LatestScrapedAt)!.Value,
                    ProductCount = g.Count(),
                })
                .ToListAsync(ct);

            var stale = sources
                .Where(s => s.LastScraped < staleBefore && s.LastScraped >= retiredBefore)
                .OrderBy(s => s.LastScraped)
                .Select(s => new
                {
                    source = s.Source,
                    lastScraped = s.LastScraped,
                    hoursAgo = (int)(now - s.LastScraped).TotalHours,
                    productCount = s.ProductCount,
                })
                .ToList();

            var retired = sources
                .Where(s => s.LastScraped < retiredBefore)
                .Select(s => s.Source)
                .OrderBy(name => name)
                .ToList();

            var body = new
            {
                status = stale.Count == 0 ? "healthy" : "stale-sources",
                thresholdHours = staleHours,
                sourceCount = sources.Count,
                staleSources = stale,
                // For information: they don't produce a 503 (see above).
                retiredSources = retired,
            };

            // The health endpoint must NOT be cached: a cached "healthy" response
            // hides a failure. The output cache policy isn't applied anyway; this
            // header is for Cloudflare and every layer in between.
            return stale.Count == 0
                ? Results.Json(body, statusCode: StatusCodes.Status200OK)
                : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
    }
}
