using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Catalog;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Api.Endpoints;

// MONITORING endpoints of the admin panel: status summary, security events,
// failed admin operations, click report.
// Split out of AdminEndpoints.cs by panel tab (security/architecture review,
// 2026-09-26); the endpoints are unchanged, all protected by X-Admin-Key.
internal static class AdminMonitoringEndpoints
{
    public static void MapAdminMonitoringEndpoints(this WebApplication app, string? adminApiKey)
    {
        // The admin panel's "Status" screen, in ONE request.
        //
        // WHY A SEPARATE ENDPOINT: the panel comes through /admin/api/* and
        // Caddy rewrites that to /api/dev/*, so the panel CAN'T reach
        // endpoints outside /api/dev such as /api/stats or
        // /api/health/sources. Rather than a second Caddy rule there's one
        // summary endpoint: one request, and what the panel can see is
        // visible in ONE place.
        //
        // SOURCE FRESHNESS ISN'T RE-INTERPRETED HERE. The stale/retired
        // thresholds live in /api/health/sources and alerts come from there;
        // writing the same rule twice means two copies that drift apart. Only
        // RAW last-scrape times are returned; the UI interprets them.
        app.MapGet("/api/dev/status", async (AppDbContext db, CancellationToken ct) =>
        {
            // The panel should show the truth: hidden products are part of the catalog too.
            var products = await db.Products
                .IgnoreQueryFilters()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    withNutrition = g.Count(p => p.NutritionJson != null),
                    clicks = g.Sum(p => p.ClickCount),
                    lastNutritionRun = g.Max(p => p.NutritionCheckedAt),
                })
                .FirstOrDefaultAsync(ct);

            var brandCount = await db.Brands.CountAsync(ct);

            var subscribers = await db.Subscribers
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    confirmed = g.Count(x => x.IsConfirmed && x.UnsubscribedAt == null),
                    pending = g.Count(x => !x.IsConfirmed && x.UnsubscribedAt == null),
                })
                .FirstOrDefaultAsync(ct);

            // A source is COALESCE(Seller, Brand.Name), the same definition as
            // the health endpoint. The 12 stalest are enough; the panel is a
            // quick look, not a monitoring tool.
            var sources = await db.Products
                .IgnoreQueryFilters()
                .Where(p => p.LatestScrapedAt != null)
                .GroupBy(p => p.Seller ?? p.Brand!.Name)
                .Select(g => new { source = g.Key, lastScraped = g.Max(p => p.LatestScrapedAt) })
                .OrderBy(x => x.lastScraped)
                .Take(12)
                .ToListAsync(ct);

            var dayAgo = DateTimeOffset.UtcNow.AddDays(-1);
            var eventSummary = await db.SecurityEvents
                .Where(x => x.OccurredAt >= dayAgo)
                .GroupBy(x => x.Kind)
                .Select(g => new { kind = g.Key, count = g.Count() })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                products = new
                {
                    total = products?.total ?? 0,
                    withNutrition = products?.withNutrition ?? 0,
                    brandCount,
                },
                clickTotal = products?.clicks ?? 0,
                nutrition = new
                {
                    lastRun = products?.lastNutritionRun,
                    nextRun = products?.lastNutritionRun?.AddDays(2),
                },
                subscribers = new
                {
                    confirmed = subscribers?.confirmed ?? 0,
                    pending = subscribers?.pending ?? 0,
                },
                sources,
                lastDayEvents = eventSummary,
            });
        }).RequireAdminKey(adminApiKey);

        // WHY admin operations failed. Shown on the panel's "Events" tab NEXT
        // TO the security events but SEPARATELY: they answer different
        // questions ("who is attacking me" vs "why didn't my action work"),
        // and one list would pollute the record that backs an abuse report
        // with our own mistakes.
        app.MapGet("/api/dev/admin-failures", async (
            AppDbContext db, int? limit, int? days, CancellationToken ct) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, 365));
            var take = Math.Clamp(limit ?? 100, 1, 500);

            var records = await db.AdminOperationFailures.AsNoTracking()
                .Where(x => x.OccurredAt >= since)
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToListAsync(ct);

            return Results.Ok(records);
        }).RequireAdminKey(adminApiKey);

        // Security events: the source of the panel's event feed.
        //
        // THE SUMMARY IS QUERIED SEPARATELY FROM THE LIST: the list is cut by
        // `take`, and counting a truncated list misleads ("3 attacks" when
        // there are really 3,000).
        app.MapGet("/api/dev/security-events", async (
            AppDbContext db, string? kind, string? ip, int? limit, int? days, CancellationToken ct) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, 365));
            var take = Math.Clamp(limit ?? 200, 1, 1000);

            var query = db.SecurityEvents.AsNoTracking().Where(x => x.OccurredAt >= since);
            if (!string.IsNullOrWhiteSpace(kind))
                query = query.Where(x => x.Kind == kind);
            if (!string.IsNullOrWhiteSpace(ip))
                query = query.Where(x => x.Ip == ip);

            var events = await query
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToListAsync(ct);

            var summary = await query
                .GroupBy(x => x.Kind)
                .Select(g => new { kind = g.Key, count = g.Count() })
                .ToListAsync(ct);

            // The first thing an abuse report asks: which address, how often, when.
            var topIps = await query
                .GroupBy(x => x.Ip)
                .Select(g => new
                {
                    ip = g.Key,
                    count = g.Count(),
                    firstSeen = g.Min(x => x.OccurredAt),
                    lastSeen = g.Max(x => x.OccurredAt),
                })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync(ct);

            return Results.Ok(new { events, summary, topIps });
        }).RequireAdminKey(adminApiKey);

        // For a "here's how many clicks we sent you" report to brands.
        // ClickCount is an undated, cumulative counter (no per-click timestamp),
        // so these are totals since launch. For a weekly report, run this on the
        // same day each week and subtract the previous week's number.
        app.MapGet("/api/dev/click-report", async (AppDbContext db, CancellationToken ct) =>
        {
            var report = await db.Products
                .Where(p => p.Brand!.IsActive)
                .GroupBy(p => p.Brand!.Name)
                .Select(g => new
                {
                    Brand = g.Key,
                    TotalClicks = g.Sum(p => p.ClickCount),
                    ProductCount = g.Count(),
                    TopProducts = g.OrderByDescending(p => p.ClickCount).Take(5).Select(p => new { p.Name, p.ClickCount }),
                })
                .OrderByDescending(r => r.TotalClicks)
                .ToListAsync(ct);

            return Results.Ok(report);
        }).RequireAdminKey(adminApiKey);
    }
}
