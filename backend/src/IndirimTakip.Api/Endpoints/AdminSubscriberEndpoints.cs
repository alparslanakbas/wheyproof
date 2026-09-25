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

// SUBSCRIBER endpoints of the admin panel: digest sending, the subscriber list
// and its actions, email capacity.
// Split out of AdminEndpoints.cs by panel tab (security/architecture review,
// 2026-09-26); the endpoints are unchanged, all protected by X-Admin-Key.
internal static class AdminSubscriberEndpoints
{
    public static void MapAdminSubscriberEndpoints(this WebApplication app, string? adminApiKey)
    {
        // The digest is sent automatically by DigestBackgroundService; this
        // endpoint stays for manual and test sends.
        app.MapPost("/api/dev/send-digest", async (DigestService digest, IConfiguration config, CancellationToken ct) =>
        {
            // The same address as the scheduled send. Built from Host, a digest
            // triggered through the panel path (www) got unsubscribe links that
            // fell through to the frontend and answered 404 (security review,
            // 2026-09-25).
            var baseUrl = (config["PublicBaseUrl"] ?? "https://api.wheyproof.com").TrimEnd('/');
            var result = await digest.SendDigestAsync(baseUrl, ct);
            return Results.Ok(result);
        }).RequireAdminKey(adminApiKey);

        // This week's digest rendered with live deals, for review; sends nothing.
        // The API's default CSP only allows images from itself and the site, but
        // the email shows product images from store CDNs, so this response
        // allows https images. Scripts stay blocked: it is static email HTML.
        app.MapGet("/api/dev/digest/preview", async (DigestService digest, HttpContext http, CancellationToken ct) =>
        {
            var html = await digest.BuildPreviewAsync(ct);
            if (html is null)
                return Results.NotFound("No real discount to feature right now, so this week's digest would be skipped.");

            http.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'unsafe-inline'; img-src https: data:; base-uri 'none'; frame-ancestors 'none'";
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireAdminKey(adminApiKey);

        // --- Newsletter subscribers (admin panel) ---
        //
        // THE SUMMARY IS COUNTED SEPARATELY FROM THE LIST, as with security
        // events: the list is capped, and counting a capped list would
        // understate the total.
        app.MapGet("/api/dev/subscribers", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Subscribers
                .AsNoTracking()
                .OrderByDescending(s => s.SubscribedAt)
                .Take(1000)
                .Select(s => new
                {
                    s.Id,
                    s.Email,
                    s.IsConfirmed,
                    s.SubscribedAt,
                    s.ConfirmedAt,
                    s.UnsubscribedAt,
                    s.LastConfirmationEmailSentAt,
                    s.LastDigestSentAt,
                    // The same table backs price alerts and the watchlist; these
                    // counts show what a deactivation would also affect.
                    watchCount = db.ProductWatches.Count(w => w.SubscriberId == s.Id),
                    favoriteCount = db.ProductFavorites.Count(f => f.SubscriberId == s.Id),
                })
                .ToListAsync(ct);

            var subscribers = rows.Select(s => new
            {
                s.Id,
                s.Email,
                status = SubscriberService.StatusOf(s.IsConfirmed, s.UnsubscribedAt).ToString().ToLowerInvariant(),
                s.SubscribedAt,
                s.ConfirmedAt,
                s.UnsubscribedAt,
                s.LastConfirmationEmailSentAt,
                s.LastDigestSentAt,
                s.watchCount,
                s.favoriteCount,
            });

            var summary = await db.Subscribers
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    active = g.Count(x => x.IsConfirmed && x.UnsubscribedAt == null),
                    pending = g.Count(x => !x.IsConfirmed && x.UnsubscribedAt == null),
                    unsubscribed = g.Count(x => x.UnsubscribedAt != null),
                })
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                subscribers,
                summary = summary ?? new { total = 0, active = 0, pending = 0, unsubscribed = 0 },
            });
        }).RequireAdminKey(adminApiKey);

        app.MapPost("/api/dev/subscribers/{id:int}/deactivate", async (int id, SubscriberService subscribers, CancellationToken ct) =>
            await subscribers.DeactivateAsync(id, ct)
                ? Results.Ok(new { id, status = "unsubscribed" })
                : Results.NotFound($"Subscriber {id} not found.")).RequireAdminKey(adminApiKey);

        app.MapPost("/api/dev/subscribers/{id:int}/send-confirmation", async (
            int id, SubscriberService subscribers, IConfiguration config, CancellationToken ct) =>
        {
            // NOT the request's host: the panel reaches this endpoint through
            // www.wheyproof.com/admin/api, and www doesn't route /api/subscribe
            // to the backend, so a link built from it would be a dead end.
            var confirmBaseUrl = (config["PublicBaseUrl"] ?? "https://api.wheyproof.com").TrimEnd('/');

            return await subscribers.ResendConfirmationAsync(id, confirmBaseUrl, ct) switch
            {
                AdminConfirmationResult.Sent => Results.Ok(new { message = "Confirmation email sent." }),
                AdminConfirmationResult.NotFound => Results.NotFound($"Subscriber {id} not found."),
                AdminConfirmationResult.AlreadyActive => Results.Conflict("This subscriber is already active."),
                AdminConfirmationResult.CoolingDown => Results.Json(
                    new { message = "A confirmation email went out less than 5 minutes ago. Try again later." },
                    statusCode: StatusCodes.Status429TooManyRequests),
                _ => Results.Json(
                    new { message = "The email provider didn't accept the message. Check the backend logs." },
                    statusCode: StatusCodes.Status502BadGateway),
            };
        }).RequireAdminKey(adminApiKey);

        // Email capacity report. The provider's daily quota is shared between the
        // digest and transactional mail (confirmation, price alert, watchlist
        // recovery), so when the quota fills up silently, a new subscriber
        // never gets the confirmation email, with no visible error anywhere.
        // This endpoint shows how close that limit is.
        app.MapGet("/api/dev/email-stats", async (AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var intervalDays = config.GetValue("Digest:IntervalDays", 7);
            var dailyQuota = config.GetValue("Digest:DailyQuota", 200);
            var now = DateTimeOffset.UtcNow;
            var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var dueBefore = now.AddDays(-intervalDays);

            var activeSubscribers = await db.Subscribers
                .CountAsync(s => s.IsConfirmed && s.UnsubscribedAt == null, ct);
            var pendingConfirmation = await db.Subscribers
                .CountAsync(s => !s.IsConfirmed && s.UnsubscribedAt == null, ct);
            var sentToday = await db.Subscribers
                .CountAsync(s => s.LastDigestSentAt >= todayStart, ct);
            var awaitingDigest = await db.Subscribers
                .CountAsync(s => s.IsConfirmed && s.UnsubscribedAt == null
                    && (s.LastDigestSentAt == null || s.LastDigestSentAt < dueBefore), ct);

            // How many days one digest round spans: past the quota, the rest
            // roll over to the next day (see DigestService).
            var daysPerRound = (int)Math.Ceiling(activeSubscribers / (double)dailyQuota);

            return Results.Ok(new
            {
                activeSubscribers,
                pendingConfirmation,
                digestIntervalDays = intervalDays,
                dailyDigestQuota = dailyQuota,
                sentToday,
                remainingQuotaToday = Math.Max(0, dailyQuota - sentToday),
                awaitingDigest,
                daysPerRound,
                // Once a round takes longer than the send interval, some
                // subscribers start missing rounds; that's the practical ceiling.
                maxSubscribersAtCurrentSettings = dailyQuota * intervalDays,
                capacityUsedPercent = Math.Round(activeSubscribers * 100.0 / (dailyQuota * intervalDays), 2),
            });
        }).RequireAdminKey(adminApiKey);
    }
}
