using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Api.Endpoints;

// Visitor engagement: price alerts, watchlist, click counter and "was this
// helpful" votes. All rate limited.
internal static class EngagementEndpoints
{
    public static void MapEngagementEndpoints(this WebApplication app, string frontendBaseUrl)
    {
        // Price alert: if this product's price really drops on a later scrape, a
        // one-time notification email goes out (see ProductWatchNotifier).
        app.MapPost("/api/products/{id:int}/watch", async (int id, WatchProductRequest request, ProductWatchService watchService, HttpContext http, CancellationToken ct) =>
        {
            if (!EndpointHelpers.IsValidEmail(request.Email))
                return Results.BadRequest(new { message = "Enter a valid email address." });

            var confirmBaseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var success = await watchService.WatchAsync(id, request, confirmBaseUrl, ct);
            return success ? Results.Ok(new { message = "We'll let you know when the price drops." }) : Results.NotFound();
        }).RequireRateLimiting("EmailSensitive").LogSensitiveRequest(app.Logger);

        // Watchlist: no account or login. The first add takes an email; the
        // returned token is kept in the browser and used on later requests.
        // Unlike price alerts it sends no email, so there's no confirmation flow.
        app.MapPost("/api/products/{id:int}/favorite", async (int id, FavoriteRequest request, FavoriteService favorites, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.Token) && !EndpointHelpers.IsValidEmail(request.Email))
                return Results.BadRequest(new { message = "Enter a valid email address." });

            var (success, token, recoverySent) = await favorites.AddAsync(id, request.Token, request.Email, frontendBaseUrl, ct);
            return success ? Results.Ok(new { token, recoverySent }) : Results.NotFound();
        }).RequireRateLimiting("EmailSensitive").LogSensitiveRequest(app.Logger);

        app.MapDelete("/api/products/{id:int}/favorite", async (int id, string token, FavoriteService favorites, CancellationToken ct) =>
        {
            var removed = await favorites.RemoveAsync(id, token, ct);
            return removed ? Results.Ok() : Results.NotFound();
        }).RequireRateLimiting("General").LogSensitiveRequest(app.Logger);

        app.MapGet("/api/favorites", async (string token, FavoriteService favorites, DealsQueryService deals, CancellationToken ct) =>
        {
            var productIds = await favorites.GetFavoriteProductIdsAsync(token, ct);
            if (productIds is null)
                return Results.NotFound();

            var result = await deals.GetDealsByIdsAsync(productIds, cancellationToken: ct);
            return Results.Ok(result);
        });

        // Watchlist recovery: someone who lost the browser token (another device
        // or browser, cleared site data) enters their email and gets a link with
        // the token. To prevent email enumeration the response is the same
        // whether or not the address is registered; only an invalid format gets
        // a distinct error.
        app.MapPost("/api/favorites/recover", async (RecoverFavoritesRequest request, FavoriteService favorites, CancellationToken ct) =>
        {
            if (!EndpointHelpers.IsValidEmail(request.Email))
                return Results.BadRequest(new { message = "Enter a valid email address." });

            await favorites.SendRecoveryEmailAsync(request.Email, frontendBaseUrl, ct);
            return Results.Ok(new { message = "If this email is registered, we've sent your watchlist link." });
        }).RequireRateLimiting("EmailSensitive").LogSensitiveRequest(app.Logger);

        // Store click. The link goes STRAIGHT to the store (see DealDto.StoreUrl),
        // so /go/{id} can't count it; a beacon is sent here at click time.
        //
        // Side benefit: the counter only increases in real browsers that run
        // JavaScript. On /go that needed user-agent bot filtering (the counter
        // feeds the click report shared with brands, and bot traffic would make
        // it misleading).
        //
        // No body expected: navigator.sendBeacon is called with an empty body so
        // the request stays "simple" and triggers no CORS preflight; a preflight
        // could be cancelled as the page leaves for the store, losing the count.
        app.MapPost("/api/products/{id:int}/click", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            // Increment in a single statement: no need to load the product, and
            // no lost updates on concurrent clicks.
            var affected = await db.Products
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.ClickCount, p => p.ClickCount + 1), ct);

            return affected == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireRateLimiting("General");

        // "Was this helpful?" vote: a simple trust signal. No auth and no record
        // of who voted; the frontend prevents repeat votes with localStorage.
        app.MapPost("/api/products/{id:int}/vote", async (int id, VoteRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([id], ct);
            if (product is null)
                return Results.NotFound();

            if (request.Helpful)
                product.HelpfulYesCount++;
            else
                product.HelpfulNoCount++;

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }).RequireRateLimiting("General");
    }
}
