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

// Admin endpoints (/api/dev/*). ALL of them require X-Admin-Key (or the
// admin session); unprotected, anyone could trigger scrapes and add fake
// coupons.
internal static class AdminEndpoints
{
    // 404 for an unknown product, 400 with the reason for a refused value (the
    // panel shows it as-is), otherwise the list cache is refreshed: without it
    // the change stays invisible on the site for the output cache's hour.
    private static async Task<IResult> ManualEditResponse(ManualEditResult result, int id, IPublicCacheRefresher cache, CancellationToken ct)
    {
        if (!result.Found)
            return Results.NotFound($"Product {id} not found.");
        if (!result.Accepted)
            return Results.BadRequest(new { message = result.Reason });

        await cache.RefreshAsync(ct);
        return Results.Ok(new { rowsUpdated = result.RowsUpdated });
    }

    // Brands whose product pages the detail backfill reads nutrition from.
    private static readonly string[] PageNutritionBrands =
        IndirimTakip.Infrastructure.Scraping.Shopify.ShopifyStores.All
            .Where(s => s.NutritionOnPage && !s.IsRetailer)
            .Select(s => s.BrandName)
            .ToArray();

    public static void MapAdminEndpoints(this WebApplication app, string? adminApiKey)
    {
        // Triggers a scrape by hand. The work runs IN THE BACKGROUND and the
        // endpoint returns 202 right away.
        //
        // WHY: running the scrape inside the request was useless for long
        // sources. Cloudflare waits ~100-125 seconds for the origin and then
        // returns 524; when the connection drops ASP.NET cancels the request,
        // the CancellationToken fires and NOTHING IS SAVED. Hundreds of
        // requests can hit a store with not a single product written.
        //
        // Two subtleties:
        //   • The request scope is disposed as soon as the response returns, so
        //     the background job opens ITS OWN scope and resolves the scraper there.
        //   • The cancellation token is tied to the APPLICATION lifetime, not the
        //     request; otherwise the same bug would come back in another form.
        //
        // Protection against scraping the same source concurrently already
        // lives in ScrapeIngestionService (a second trigger is rejected).
        app.MapPost("/api/dev/ingest/{brand}", (
            string brand,
            IEnumerable<IBrandScraper> scrapers,
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime lifetime,
            ILoggerFactory loggerFactory) =>
        {
            var scraper = scrapers.FirstOrDefault(s => s.BrandName.Equals(brand, StringComparison.OrdinalIgnoreCase));
            if (scraper is null)
                return Results.NotFound($"No scraper found for '{brand}'.");

            var brandName = scraper.BrandName;
            var logger = loggerFactory.CreateLogger("ManualScrape");

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<ScrapeIngestionService>();
                var scoped = scope.ServiceProvider.GetServices<IBrandScraper>()
                    .First(s => s.BrandName == brandName);

                try
                {
                    logger.LogInformation("Manual scrape started: {Brand}.", brandName);
                    var count = await ingestion.IngestAsync(scoped, lifetime.ApplicationStopping);

                    // The data changed: drop the cache and warm the hot endpoints.
                    // Manual scrapes mostly run right after a deploy, exactly
                    // when a visitor would hit a cold cache.
                    // The price summary goes FIRST: cache warming reads those
                    // fields, and the reverse order would cache the old summary.
                    await scope.ServiceProvider.GetRequiredService<PriceSummaryRefresher>()
                        .RefreshAsync(lifetime.ApplicationStopping);

                    await scope.ServiceProvider.GetRequiredService<IPublicCacheRefresher>()
                        .RefreshAsync(lifetime.ApplicationStopping);

                    logger.LogInformation("Manual scrape finished: {Brand}, {Count} products.", brandName, count);
                }
                catch (Exception ex)
                {
                    // Never swallowed: a background job dying silently is exactly
                    // the "I thought it ran but there's no data" case this
                    // endpoint exists to solve.
                    logger.LogError(ex, "Manual scrape FAILED: {Brand}.", brandName);
                }
            });

            // Follow the result in the logs and the database; the client doesn't
            // need to keep the connection open.
            return Results.Accepted(value: new
            {
                brand = brandName,
                status = "scrape started in the background",
                howToFollow = "docker compose logs backend | grep 'Manual scrape'",
            });
        }).RequireAdminKey(adminApiKey);

        // Coupons aren't scraped: they're checked by hand and added here.
        app.MapPost("/api/dev/coupons", async (CreateCouponRequest request, CouponService coupons, CancellationToken ct) =>
        {
            if (!request.HasExactlyOneTarget)
                return Results.BadRequest("A coupon must belong to exactly one brand or one seller.");
            // The code is deliberately OPTIONAL: not every promotion has a code
            // to enter (e.g. an automatic first-order discount for members). The
            // description is required: it's the only text people see.
            if (string.IsNullOrWhiteSpace(request.Description))
                return Results.BadRequest("The coupon description can't be empty.");

            var result = await coupons.CreateAsync(request, ct);
            return result is null ? Results.NotFound($"No brand named '{request.BrandName}'.") : Results.Ok(result);
        }).RequireAdminKey(adminApiKey);

        // Edits or deactivates a coupon (an expired or wrong one, for example).
        app.MapPut("/api/dev/coupons/{id:int}", async (int id, UpdateCouponRequest request, CouponService coupons, CancellationToken ct) =>
        {
            var result = await coupons.UpdateAsync(id, request, ct);
            return result is null ? Results.NotFound($"Coupon {id} not found.") : Results.Ok(result);
        }).RequireAdminKey(adminApiKey);

        // Removes out-of-scope products by hand. Cascade delete removes their
        // PriceHistory/ProductFavorite/ProductWatch rows too. With the scraper
        // filter in place, a deleted product doesn't come back on the next scrape.
        app.MapDelete("/api/dev/products/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            // Explicit: a hidden product must be deletable too.
            var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (product is null) return Results.NotFound($"Product {id} not found.");
            db.Products.Remove(product);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }).RequireAdminKey(adminApiKey);

        app.MapPost("/api/dev/articles", async (CreateArticleRequest request, ArticleService articles, CancellationToken ct) =>
        {
            var result = await articles.CreateAsync(request, ct);
            return result is null ? Results.Conflict($"The slug '{request.Slug}' is already taken.") : Results.Ok(result);
        }).RequireAdminKey(adminApiKey);

        // Edits an existing article; a partial update, fields not sent stay as they are.
        app.MapPut("/api/dev/articles/{slug}", async (string slug, UpdateArticleRequest request, ArticleService articles, CancellationToken ct) =>
        {
            var result = await articles.UpdateAsync(slug, request, ct);
            return result is null ? Results.NotFound($"No article with slug '{slug}'.") : Results.Ok(result);
        }).RequireAdminKey(adminApiKey);

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

        // Description backfill runs automatically in
        // DescriptionBackfillBackgroundService; this is the manual trigger.
        app.MapPost("/api/dev/backfill-descriptions", async (ProductDetailBackfillService backfill, CancellationToken ct) =>
        {
            var updated = await backfill.BackfillAsync(ct);
            return Results.Ok(new { updatedCount = updated });
        }).RequireAdminKey(adminApiKey);

        // Refreshes the brands' star ratings by hand. The real mechanism is
        // RatingRefreshBackgroundService (every 6 hours, oldest checks first);
        // this endpoint is for the first fill and spot checks.
        app.MapPost("/api/dev/refresh-ratings", async (ProductRatingRefreshService ratings, int? max, CancellationToken ct) =>
        {
            var updated = await ratings.RefreshAsync(max, ct);
            return Results.Ok(new { updatedCount = updated });
        }).RequireAdminKey(adminApiKey);

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

        // --- Brand and product visibility (admin panel) ---
        //
        // Brands: Brand.IsActive has always existed and DealsQueryService checks
        // it in every public query; this only adds the switch.
        //
        // Products: Product.IsActive plus a global query filter in
        // AppDbContext. The filter lives in one place, so a hidden product
        // drops out of every page and the sitemap on its own.
        app.MapGet("/api/dev/brands", async (AppDbContext db, CancellationToken ct) =>
        {
            var brands = await db.Brands
                .AsNoTracking()
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.IsActive,
                    // Hidden products count too: "how many products does it
                    // have" should be answered with the whole catalog.
                    productCount = db.Products.IgnoreQueryFilters().Count(p => p.BrandId == b.Id),
                    hiddenProducts = db.Products.IgnoreQueryFilters().Count(p => p.BrandId == b.Id && !p.IsActive),
                })
                .OrderBy(b => b.Name)
                .ToListAsync(ct);

            return Results.Ok(brands);
        }).RequireAdminKey(adminApiKey);

        app.MapPut("/api/dev/brands/{id:int}", async (
            int id, VisibilityRequest request, AppDbContext db, IPublicCacheRefresher cache, CancellationToken ct) =>
        {
            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (brand is null)
                return Results.NotFound($"Brand {id} not found.");

            brand.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);

            // Without a cache refresh the change stays invisible on the site
            // for an hour (the output cache TTL) and looks like it didn't work.
            await cache.RefreshAsync(ct);
            return Results.Ok(new { brand.Id, brand.Name, brand.IsActive });
        }).RequireAdminKey(adminApiKey);

        // The catalog has thousands of products; listing REQUIRES a search.
        // The filters list without a search: "missing nutrition" and
        // "uncategorised" are the worklists for entering data by hand.
        //
        // PAGED, WITH A TOTAL. It used to return the first 200 rows and stop: with
        // "missing nutrition" on, thousands of rows matched and everything after
        // row 200 (sorted by brand) could not be reached, with no sign the list
        // was cut. Id is the last sort key so a page never repeats or skips a row.
        app.MapGet("/api/dev/products", async (
            AppDbContext db, string? search, bool? hiddenOnly, bool? missingNutrition, bool? uncategorised,
            bool? needsManual, bool? manuallyEntered, int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = db.Products.IgnoreQueryFilters().AsNoTracking();

            if (hiddenOnly == true)
                query = query.Where(p => !p.IsActive);
            if (missingNutrition == true)
                query = query.Where(p => p.NutritionJson == null);
            if (uncategorised == true)
                query = query.Where(p => p.Category == null);

            // NO AUTOMATIC SOURCE LEFT: the rows only a person can fill. Typing a
            // panel the label reader fills a few hours later is wasted work, so
            // every route that can still fill a row keeps it out of this list:
            // - a label image not yet read at its current URL (the reader's queue);
            // - a product page whose store prints the panel as readable text or JSON
            //   (ShopifyStore.NutritionOnPage) and that the detail backfill hasn't
            //   checked yet. It only visits the brand's own store (Seller == null).
            // The regular crawl has already run for every row, so a row it didn't
            // fill it won't fill later.
            // ENTERED BY HAND, so the person can find their own work again. Both
            // flags count, since both are that person's decision; a product closed
            // with "no panel on this product" belongs here too, empty table and all.
            if (manuallyEntered == true)
                query = query.Where(p => p.NutritionIsManual || p.CategoryIsManual);

            if (needsManual == true)
            {
                query = query.Where(p => p.NutritionJson == null
                    && (p.NutritionLabelImageUrl == null || p.NutritionLabelReadUrl == p.NutritionLabelImageUrl)
                    && !(p.Seller == null && p.NutritionCheckedAt == null && PageNutritionBrands.Contains(p.Brand!.Name)));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                // Postgres lower()/ILIKE fold case by the database locale, and
                // this database uses C.UTF-8 (builtin): no folding for non-ASCII
                // letters. So the search is tried both as typed and lower-cased.
                var raw = search.Trim();
                var lower = raw.ToLowerInvariant();
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, "%" + raw + "%")
                    || EF.Functions.ILike(p.Name, "%" + lower + "%")
                    || EF.Functions.ILike(p.Brand!.Name, "%" + raw + "%"));
            }
            // A NEW FILTER MUST BE ADDED HERE TOO: one missing from this condition
            // silently returns an empty list, because the query never runs.
            else if (hiddenOnly != true && missingNutrition != true && uncategorised != true
                && needsManual != true && manuallyEntered != true)
            {
                // Without a search the list would be uselessly large.
                return Results.Ok(new { items = Array.Empty<object>(), total = 0, page = 1, pageSize = 0 });
            }

            var size = Math.Clamp(pageSize ?? 50, 10, 100);
            var current = Math.Max(1, page ?? 1);
            var total = await query.CountAsync(ct);

            var products = await query
                .OrderBy(p => p.Brand!.Name)
                .ThenBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Skip((current - 1) * size)
                .Take(size)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    brand = p.Brand!.Name,
                    p.Seller,
                    p.IsActive,
                    p.LatestPrice,
                    p.Category,
                    p.CategoryIsManual,
                    p.NutritionJson,
                    p.NutritionIsManual,
                    p.ServingSizeGrams,
                    // Why the label reader didn't fill it ("rejected: supplement facts:
                    // unknown row name 'Vitamin Be'"): shown in the editor.
                    p.NutritionLabelStatus,
                })
                .ToListAsync(ct);

            return Results.Ok(new { items = products, total, page = current, pageSize = size });
        }).RequireAdminKey(adminApiKey);

        app.MapPut("/api/dev/products/{id:int}", async (
            int id, VisibilityRequest request, AppDbContext db, IPublicCacheRefresher cache, CancellationToken ct) =>
        {
            var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (product is null)
                return Results.NotFound($"Product {id} not found.");

            product.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);

            // Same reason: hiding a product must refresh the list cache.
            await cache.RefreshAsync(ct);
            return Results.Ok(new { product.Id, product.Name, product.IsActive });
        }).RequireAdminKey(adminApiKey);

        // --- Category and nutrition entered by hand (see ManualProductDataService) ---
        app.MapPut("/api/dev/products/{id:int}/category", async (
            int id, ProductCategoryRequest request, ManualProductDataService data, IPublicCacheRefresher cache, CancellationToken ct) =>
            await ManualEditResponse(await data.SetCategoryAsync(id, request.Category, ct), id, cache, ct))
            .RequireAdminKey(adminApiKey);

        app.MapPut("/api/dev/products/{id:int}/nutrition", async (
            int id, ManualNutritionRequest request, ManualProductDataService data, IPublicCacheRefresher cache, CancellationToken ct) =>
            await ManualEditResponse(await data.SetNutritionAsync(id, request, ct), id, cache, ct))
            .RequireAdminKey(adminApiKey);

        app.MapDelete("/api/dev/products/{id:int}/nutrition", async (
            int id, ManualProductDataService data, IPublicCacheRefresher cache, CancellationToken ct) =>
            await ManualEditResponse(await data.ClearNutritionAsync(id, ct), id, cache, ct))
            .RequireAdminKey(adminApiKey);

        // Coupon list for the panel's editing screen. The public /api/coupons
        // endpoint returns ONLY active, unexpired coupons (what visitors should
        // see). The panel must show inactive and expired ones too, or a coupon
        // would vanish from the admin screen once turned off and could never
        // be turned back on.
        app.MapGet("/api/dev/coupons", async (AppDbContext db, CancellationToken ct) =>
        {
            var coupons = await db.Coupons
                .AsNoTracking()
                .Include(c => c.Brand)
                .OrderByDescending(c => c.IsActive)
                .ThenByDescending(c => c.LastVerifiedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Code,
                    c.Description,
                    c.BrandId,
                    brandName = c.Brand != null ? c.Brand.Name : null,
                    c.Seller,
                    c.ValidUntil,
                    c.LastVerifiedAt,
                    c.IsActive,
                })
                .ToListAsync(ct);

            return Results.Ok(coupons);
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

        // --- Nutrition label reading ---
        //
        // PILOT: reads label images with the real model and returns what it read
        // and whether validation passed, WITHOUT writing anything. Accuracy (by
        // comparing a sample against the images) and token cost are measured here
        // before the background job is switched on. Runs sequentially: a few
        // dozen images, and the request stays within Cloudflare's ~100 s limit
        // only for small batches, hence the cap.
        app.MapPost("/api/dev/nutrition-labels/pilot", async (
            NutritionLabelService labels, INutritionLabelReader reader, NutritionLabelOptions options, AppDbContext db,
            string? source, int? limit, string? model, CancellationToken ct) =>
        {
            if (!reader.IsAvailable)
                return Results.BadRequest(new { message = $"The '{reader.Engine}' label reader isn't available (binary missing or no API key)." });

            var queue = await labels.QueueAsync(Math.Clamp(limit ?? 5, 1, 10), source, ct);
            var outcomes = new List<NutritionLabelOutcome>();
            var retryLater = 0;
            foreach (var item in queue)
            {
                var outcome = await labels.ProcessAsync(item, write: false, model, ct);
                if (outcome is null) retryLater++;
                else outcomes.Add(outcome);
            }

            // How much of each source could be covered at all: products whose
            // store names a label image. The rest need another route.
            var coverage = await db.Products.IgnoreQueryFilters()
                .GroupBy(p => p.Seller ?? p.Brand!.Name)
                .Select(g => new
                {
                    source = g.Key,
                    products = g.Count(),
                    withLabelImage = g.Count(p => p.NutritionLabelImageUrl != null),
                    distinctLabelImages = g.Where(p => p.NutritionLabelImageUrl != null)
                        .Select(p => p.NutritionLabelImageUrl).Distinct().Count(),
                })
                .OrderByDescending(x => x.products)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                engine = reader.Engine,
                model = reader.Engine == "claude" ? model ?? options.Model : null,
                read = outcomes.Count,
                accepted = outcomes.Count(o => o.Accepted),
                retryLater,
                inputTokens = outcomes.Sum(o => o.InputTokens),
                outputTokens = outcomes.Sum(o => o.OutputTokens),
                outcomes,
                coverage,
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

        // Re-reads stored descriptions and fills missing ServingSizeGrams in one
        // go. No requests to stores (a pure database job), so no re-scrape is
        // needed. Later scrapes and backfills do the same extraction
        // automatically; this endpoint only completes the history.
        app.MapPost("/api/dev/backfill-serving-sizes", async (AppDbContext db, CancellationToken ct) =>
        {
            var candidates = await db.Products
                .Where(p => p.ServingSizeGrams == null && p.Description != null)
                .ToListAsync(ct);

            var updated = 0;
            foreach (var product in candidates)
            {
                var grams = ProductAttributeParser.ExtractServingSizeGrams(product.Description);
                if (grams is null)
                    continue;

                product.ServingSizeGrams = grams;
                updated++;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { candidateCount = candidates.Count, updatedCount = updated });
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

        // Submits EVERY sitemap URL to search engines (IndexNow). Normally only
        // new products are submitted; this endpoint is for the first setup and
        // bulk resubmission.
        app.MapPost("/api/dev/indexnow/submit-all", async (
            DealsQueryService deals, IndexNowClient indexNow, IConfiguration config, CancellationToken ct) =>
        {
            if (!indexNow.IsEnabled)
                return Results.BadRequest(new { message = "IndexNow is disabled or has no key configured." });

            var frontendBaseUrl = (config["FrontendBaseUrl"] ?? "https://www.wheyproof.com").TrimEnd('/');
            var entries = await deals.GetSitemapEntriesAsync(ct);

            var urls = new List<string> { frontendBaseUrl };
            urls.AddRange(entries.Select(e => $"{frontendBaseUrl}/product/{e.Id}/{Slugifier.Slugify(e.Name)}"));

            var sent = await indexNow.SubmitAsync(urls, ct);
            return Results.Ok(new { submitted = sent, total = urls.Count });
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

internal record VisibilityRequest(bool IsActive);

/// <summary>A category slug, or null to hand the product back to the automatic category.</summary>
internal record ProductCategoryRequest(string? Category);
