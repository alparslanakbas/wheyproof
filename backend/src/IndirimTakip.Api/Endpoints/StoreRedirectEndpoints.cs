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

// Store redirect (/go). Kept for backward compatibility; store links now go
// straight to DealDto.StoreUrl.
internal static class StoreRedirectEndpoints
{
    public static void MapStoreRedirectEndpoints(this WebApplication app)
    {
        // Affiliate plumbing: product links pass through here so affiliate ids
        // are easy to add. It counts the click and redirects to the store with 302.
        app.MapGet("/go/{productId:int}", async (int productId, HttpContext http, AppDbContext db,
            IOptions<AffiliateOptions> affiliateOptions, CancellationToken ct) =>
        {
            var product = await db.Products.FindAsync([productId], ct);
            if (product is null)
                return Results.NotFound();

            // The redirect always happens, but the click COUNTER doesn't move for
            // search engine bots: it feeds the click report shared with brands,
            // and bot traffic would make that data misleading. The frontend's
            // redirect layer sets the marker (see server.ts), since it already
            // sees the user agent.
            if (http.Request.Headers["X-Bot-Request"] != "1")
            {
                product.ClickCount++;
                await db.SaveChangesAsync(ct);
            }

            // For stores with an affiliate program the link carries our tracking
            // (a query pair or the network's redirect, chosen by the store's host);
            // the store reads it and attributes the sale to us. Rules come from
            // configuration (never the repo); a store without one keeps the URL
            // as is. Bot requests don't get it either, for the same reason as the
            // click counter: not to inflate the store's statistics.
            var url = product.Url;
            if (http.Request.Headers["X-Bot-Request"] != "1")
            {
                url = AffiliateLinkBuilder.Apply(url, affiliateOptions.Value);
            }

            return Results.Redirect(url, permanent: false);
        }).RequireRateLimiting("General");
    }
}
