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

// Catalog read endpoints: deal/product lists, brand and category statistics,
// sitemap. All of them use the public data cache.
internal static class DealsEndpoints
{
    public static void MapDealsEndpoints(this WebApplication app, string cachePolicy)
    {
        // /api/deals, /api/products and /api/store-deals accept the same query
        // parameters and differ only in the onlyDiscounted/onlyStoreDiscounted
        // flags, so they are mapped from one place instead of three near-copies.
        void MapDealsQueryEndpoint(string route, bool onlyDiscounted, bool onlyStoreDiscounted)
        {
            app.MapGet(route, async (
                // sellers: where the product is bought (not the same as the brand).
                // The "brand's own store" label is mapped to NULL in DealsQueryService.
                DealsQueryService deals, string[]? brands, string[]? categories, string[]? sellers, string? search,
                decimal? minPrice, decimal? maxPrice, int? days, string? sortBy, int? page, int? pageSize,
                // Pages looking for one ingredient (e.g. the beta-alanine dose
                // calculator) can TURN OFF synonym expansion: a search for
                // "alanine" returned the WHOLE amino acids category, because that
                // word is one of the category's keywords (arginine products showed
                // up on the beta-alanine page).
                bool? expandSynonyms,
                // The brand page sends true: if the brand has its own store, show
                // only that store (see DealsQueryService.GetDealsAsync).
                bool? preferBrandStore,
                CancellationToken ct) =>
            {
                // The window is FIXED at 30 days; days is ignored on purpose. The
                // site never sends it to these endpoints, and any other value fell
                // into the old per-product subquery path: days=31 took 6.1 s in one
                // request against 0.7 s normally, and a random parameter skipped
                // the cache too (measured on the Turkish site, 2026-09-25).
                const int windowDays = 30;
                var result = await deals.GetDealsAsync(
                    windowDays, brands, categories, sellers, EndpointHelpers.NormalizeSearch(search), minPrice, maxPrice,
                    onlyDiscounted, onlyStoreDiscounted, sortBy,
                    EndpointHelpers.NormalizePage(page), EndpointHelpers.NormalizePageSize(pageSize), ct,
                    expandSearchSynonyms: expandSynonyms ?? true,
                    preferBrandStore: preferBrandStore ?? false);
                return Results.Ok(result);
            }).CacheOutput(cachePolicy);
        }

        MapDealsQueryEndpoint("/api/deals", onlyDiscounted: true, onlyStoreDiscounted: false);
        MapDealsQueryEndpoint("/api/products", onlyDiscounted: false, onlyStoreDiscounted: false);
        // Products carrying a sale price the store itself declares (unverified).
        MapDealsQueryEndpoint("/api/store-deals", onlyDiscounted: false, onlyStoreDiscounted: true);

        // Brand comparison pages: average price per category.
        // brand1/brand2 are nullable: in minimal APIs a missing query parameter
        // can bind as null even when declared non-nullable (unlike route
        // parameters, query parameters aren't required automatically). Without
        // this check the .ToLower() call inside GetBrandComparisonAsync threw a
        // NullReferenceException and returned 500; an automated test caught it.
        app.MapGet("/api/brand-comparison", async (string? brand1, string? brand2, DealsQueryService deals, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(brand1) || string.IsNullOrWhiteSpace(brand2))
                return Results.BadRequest(new { message = "The brand1 and brand2 parameters are required." });

            var result = await deals.GetBrandComparisonAsync(brand1, brand2, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // The "best value per serving" table of the protein calculator. The
        // calculation parses the Size text, so it can't be translated to SQL and
        // runs in memory in the service; only the top N products are returned
        // (pulling the whole category pushed the SSR output to 451 KB).
        app.MapGet("/api/best-value-per-serving", async (
            string? category,
            string[]? brands,
            string? search,
            int? page,
            int? pageSize,
            DealsQueryService deals,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest(new { message = "The category parameter is required." });

            var result = await deals.GetBestValuePerServingAsync(
                category,
                brands is { Length: > 0 } ? brands : null,
                string.IsNullOrWhiteSpace(search) ? null : EndpointHelpers.NormalizeSearch(search),
                EndpointHelpers.NormalizePage(page),
                EndpointHelpers.NormalizePageSize(pageSize),
                ct);

            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Brand x category pages: the sitemap and internal links use only pairs
        // that actually have products.
        app.MapGet("/api/brand-category-pairs", async (DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetBrandCategoryPairsAsync(ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Brands directory: product count per brand in one request. The
        // directory used to sum brand-category-pairs and missed uncategorised
        // products; it now uses the same definition as the brand page.
        app.MapGet("/api/brand-product-counts", async (DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetBrandProductCountsAsync(ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Brand chips in the calculator table: only brands with at least one
        // product in that category whose price per serving can be calculated.
        app.MapGet("/api/best-value-brands", async (string? category, DealsQueryService deals, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest(new { message = "The category parameter is required." });

            var result = await deals.GetBestValueBrandsAsync(category, ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        app.MapGet("/api/filters", async (DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetFilterOptionsAsync(ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Summary numbers for the home page's live scraping strip.
        app.MapGet("/api/stats", async (DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetHomepageStatsAsync(cancellationToken: ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // The home page's "popular with shoppers" strip; ordering comes from real
        // favorite and click counters (see GetPreferredProductsAsync).
        app.MapGet("/api/preferred-products", async (DealsQueryService deals, int? count, CancellationToken ct) =>
        {
            // The strip can be narrowed by category tabs, so the client asks for a
            // wide pool; the upper bound is fixed against abuse.
            var take = Math.Clamp(count ?? 60, 1, 100);
            var result = await deals.GetPreferredProductsAsync(take, cancellationToken: ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // The brand page's overview section: original content built from our own
        // data, not copied (see DealsQueryService.GetBrandStatsAsync). With a
        // category, statistics come only from the brand's products in that
        // category (brand x category pages).
        app.MapGet("/api/brand-stats", async (string? brand, string? category, DealsQueryService deals, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(brand))
                return Results.BadRequest(new { message = "The brand parameter is required." });

            var result = await deals.GetBrandStatsAsync(
                brand, category: string.IsNullOrWhiteSpace(category) ? null : category, cancellationToken: ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // The product review page's "how this product compares in its category"
        // section; see DealsQueryService.GetCategoryPriceStatsAsync.
        app.MapGet("/api/category-price-stats", async (string? category, DealsQueryService deals, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest(new { message = "The category parameter is required." });

            var result = await deals.GetCategoryPriceStatsAsync(category, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).CacheOutput(cachePolicy);

        app.MapGet("/api/products/{id:int}", async (int id, DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetProductByIdAsync(id, cancellationToken: ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Data for sitemap.xml. The XML itself is built in the frontend's SSR
        // server (it knows its own domain); this only returns the raw entries.
        app.MapGet("/api/products/sitemap", async (DealsQueryService deals, CancellationToken ct) =>
        {
            var result = await deals.GetSitemapEntriesAsync(ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);
    }
}
