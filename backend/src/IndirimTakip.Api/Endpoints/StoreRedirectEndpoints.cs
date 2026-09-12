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

            var brandName = await db.Brands
                .Where(b => b.Id == product.BrandId)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(ct);

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

            // For brands with an affiliate program the tracking code is added to
            // the URL; the brand reads it and attributes the sale to us. Codes come
            // from configuration (never the repo); an unconfigured brand keeps the
            // URL as is. Bot requests don't get it either, for the same reason as
            // the click counter: not to inflate the brand's statistics.
            var url = product.Url;
            if (http.Request.Headers["X-Bot-Request"] != "1")
            {
                url = AffiliateLinkBuilder.Apply(url, brandName, affiliateOptions.Value);
            }

            return Results.Redirect(url, permanent: false);
        }).RequireRateLimiting("General");
    }
}
