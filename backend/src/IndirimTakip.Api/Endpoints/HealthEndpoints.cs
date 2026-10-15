using System.Globalization;
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

    /// <summary>
    /// A known failure taken out of the 503 until a date (UTC, the day
    /// INCLUDED): entries under <c>Health:Muted</c> with <c>Source</c> and
    /// <c>Until</c> ("yyyy-MM-dd"). The source name must match the body's
    /// <c>source</c> field EXACTLY.
    ///
    /// <b>WHY IT EXISTS.</b> A monitor kept red by one source goes BLIND: it is
    /// already "down", so a second source breaking brings no news. On 14 Oct
    /// 2026 Kaged put a Cloudflare bot challenge in front of its store (403
    /// from the server and from the home tunnel alike); the endpoint would
    /// have stayed red until the 30-day retirement. The sister site has the
    /// same rule since 2 Oct (Gigi's).
    ///
    /// <b>The end date is REQUIRED:</b> a mute without one gets forgotten and
    /// quietly drops the source from monitoring; after the date the source
    /// raises the alarm again by itself. A muted source stays listed in the
    /// body. An entry with a broken name or date is IGNORED, not fatal: the
    /// safe direction is the alarm going on ringing. A test checks that the
    /// production entries are valid, so a typo can't leave an alarm open that
    /// someone believes muted.
    /// </summary>
    internal sealed record SourceMute(string Source, DateOnly Until);

    internal sealed record SourceState(string Source, DateTimeOffset LastScraped, int ProductCount);

    internal sealed record MutedSource(SourceState Source, DateOnly Until);

    internal sealed record HealthClassification(
        IReadOnlyList<SourceState> Stale,
        IReadOnlyList<MutedSource> Muted,
        IReadOnlyList<string> Retired);

    internal static IReadOnlyList<SourceMute> ReadMutes(IConfiguration configuration) =>
        configuration.GetSection("Health:Muted").GetChildren()
            .Select(m => (Source: m["Source"]?.Trim(), Until: m["Until"]))
            .Where(m => !string.IsNullOrEmpty(m.Source))
            .Select(m => DateOnly.TryParseExact(m.Until, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var until)
                ? new SourceMute(m.Source!, until)
                : null)
            .OfType<SourceMute>()
            .ToList();

    /// <summary>
    /// Sorts the sources into stale ones (503), muted stale ones and retired
    /// ones. Pure and apart from the endpoint so the alarm's decision can be
    /// tested without a database: an alarm that silently stays green is worse
    /// than none.
    /// </summary>
    internal static HealthClassification Classify(
        IEnumerable<SourceState> sources, DateTimeOffset now, int staleHours, IReadOnlyList<SourceMute> mutes)
    {
        var staleBefore = now.AddHours(-staleHours);
        var retiredBefore = now.AddDays(-RetiredAfterDays);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var stale = new List<SourceState>();
        var muted = new List<MutedSource>();
        var retired = new List<string>();

        foreach (var source in sources.OrderBy(s => s.LastScraped))
        {
            if (source.LastScraped < retiredBefore)
            {
                retired.Add(source.Source);
                continue;
            }

            if (source.LastScraped >= staleBefore)
                continue;

            // Several entries for one source: the latest end date wins.
            var mute = mutes
                .Where(m => m.Source == source.Source && today <= m.Until)
                .MaxBy(m => m.Until);

            if (mute is null)
                stale.Add(source);
            else
                muted.Add(new MutedSource(source, mute.Until));
        }

        return new HealthClassification(stale, muted, retired.OrderBy(name => name).ToList());
    }

    public static void MapHealthEndpoints(this WebApplication app)
    {
        var staleHours = app.Configuration.GetValue<int?>("Health:StaleHours")
                         ?? DefaultStaleHours;
        var mutes = ReadMutes(app.Configuration);

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

            var result = Classify(
                sources.Select(s => new SourceState(s.Source, s.LastScraped, s.ProductCount)),
                now, staleHours, mutes);

            var body = new
            {
                status = result.Stale.Count == 0 ? "healthy" : "stale-sources",
                thresholdHours = staleHours,
                sourceCount = sources.Count,
                staleSources = result.Stale.Select(s => new
                {
                    source = s.Source,
                    lastScraped = s.LastScraped,
                    hoursAgo = (int)(now - s.LastScraped).TotalHours,
                    productCount = s.ProductCount,
                }).ToList(),
                // A known failure: no 503 until its end date (SourceMute).
                mutedSources = result.Muted.Select(m => new
                {
                    source = m.Source.Source,
                    lastScraped = m.Source.LastScraped,
                    hoursAgo = (int)(now - m.Source.LastScraped).TotalHours,
                    productCount = m.Source.ProductCount,
                    until = m.Until,
                }).ToList(),
                // For information: they don't produce a 503 (see above).
                retiredSources = result.Retired,
            };

            // The health endpoint must NOT be cached: a cached "healthy" response
            // hides a failure. The output cache policy isn't applied anyway; this
            // header is for Cloudflare and every layer in between.
            return result.Stale.Count == 0
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
