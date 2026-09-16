using IndirimTakip.Api.Caching;
using IndirimTakip.Api.Endpoints;
using IndirimTakip.Core.Caching;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddInfrastructure(builder.Configuration);

// We run behind proxies (Cloudflare, Caddy); the real client IP arrives in
// X-Forwarded-For. Unless KnownProxies/KnownNetworks are cleared, ASP.NET
// Core only trusts a loopback proxy and ignores the header (the rate limiter
// below partitions by IP, so the real IP matters).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Data Protection MUST BE REGISTERED BY HAND. This project uses Minimal API
// without AddControllers/AddAuthentication; those calls register Data
// Protection as a side effect, so most projects assume it's "just there".
// Here it wasn't, and the admin session endpoint failed at runtime.
//
// The keys live inside the container, so they change on every deploy and
// open sessions end. Deliberate: sessions last 12 hours anyway, and an admin
// session left open closing itself on deploy is the desired behavior.
builder.Services.AddDataProtection();

// Cloudflare Access token validator. SINGLETON: it caches the signing keys it
// downloads; re-downloading per request would be needless load.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CloudflareAccessValidator>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Endpoints that send real email (/api/subscribe, product watch, watchlist
    // recovery). Without a limit a bot can make us mail the same address
    // dozens of times a minute and get it blocklisted by the email provider.
    // 10 per 5 minutes per IP leaves room for normal use (a few watchlist
    // adds plus a recovery attempt); the per-address cooldown in
    // SubscriberService/FavoriteService is a separate layer on top.
    options.AddPolicy("EmailSensitive", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: RequestLoggingExtensions.GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

    // Admin panel sign-in attempts. The limit is DELIBERATELY tight: the
    // endpoint verifies a password and its only job is making guessing
    // useless. Every failed attempt is also recorded as a security event (401).
    options.AddPolicy("admin-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: RequestLoggingExtensions.GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        }));

    // A looser, general limit for endpoints that send no email but could be
    // used to inflate counters (/go, votes, removing watchlist items). More
    // than 60 clicks or votes a minute isn't normal for a person, and the
    // limit is generous enough not to get in the way while browsing.
    options.AddPolicy("General", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: RequestLoggingExtensions.GetClientIp(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Allowed origins come from configuration (localhost:4200 in Development),
// so each environment can be set without a code change.
const string CorsPolicy = "Frontend";
const string PublicDataCachePolicy = "PublicData";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

// Most of the server response time for SSR pages is the endpoints called
// while rendering. Their data only changes between scrapes, so an output
// cache removes repeated queries without giving up freshness.
//
// The default policy is deliberately "no caching": an endpoint only enters
// the cache when explicitly marked. Personal responses (watchlist,
// subscription confirmation) and the counting /go/{id} can't be cached by
// accident.
// With low traffic a short TTL means practically every visitor hits a cold
// cache. Data only changes with a scrape, so the TTL is long and the cache
// is purged BY TAG as soon as a scrape finishes (OutputCacheRefresher): no
// cold hits, and the data is never staler than the last scrape.
var publicCacheSeconds = builder.Configuration.GetValue("OutputCache:PublicSeconds", 3600);
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.NoCache());
    options.AddPolicy(PublicDataCachePolicy, policy => policy
        .Expire(TimeSpan.FromSeconds(publicCacheSeconds))
        // The post-scrape purge works by this tag.
        .Tag(OutputCacheRefresher.Tag)
        // Filter and paging parameters change the response entirely; without
        // all of them in the cache key, different filters would see each
        // other's results.
        .SetVaryByQuery("*")
        // HOST IS NOT PART OF THE KEY (16 Sept). SSR now reaches the API over
        // the Docker network (http://wheyproof-backend:8080). Node's fetch won't
        // send a custom Host header (measured), so internal requests arrive
        // with the container name as Host; with Host in the key they would never
        // see the warmed entries (Host: api.wheyproof.com) and every page would
        // hit the database cold. These responses don't depend on Host and the
        // API is served from one public address. The scheme STAYS in the key:
        // SSR and the warmup both send X-Forwarded-Proto: https.
        .SetVaryByHost(false));
});

// The implementation that refreshes the cache after a scrape. The interface
// lives in Core so scrapers know nothing about the HTTP server; the concrete
// type stays here.
builder.Services.AddHttpClient(nameof(OutputCacheRefresher), client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IPublicCacheRefresher, OutputCacheRefresher>();

builder.Services.Configure<AffiliateOptions>(builder.Configuration.GetSection("Affiliate"));

var app = builder.Build();

// Which stores have an affiliate link rule, by host. Rules come from .env and
// fail silently (a store without a rule just keeps its plain URL), so the
// startup log is the one place a missing or unread rule shows up. Only hosts
// and configuration key NAMES are logged: the rule values carry account keys.
{
    var affiliateOptions = app.Services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AffiliateOptions>>().Value;
    var hosts = AffiliateLinkBuilder.ConfiguredHosts(affiliateOptions).ToList();
    var keyNames = app.Configuration.GetSection("Affiliate").AsEnumerable(makePathsRelative: true)
        .Where(kv => kv.Value is not null).Select(kv => kv.Key).Order();
    app.Logger.LogInformation("Affiliate link rules loaded for {Count} stores: {Hosts} (configuration keys: {Keys})",
        hosts.Count, string.Join(", ", hosts), string.Join(", ", keyNames));
}

// /api/dev/* endpoints (manual scrapes, coupons) must not be public. A full
// user/auth system would be over-engineering; a shared key (in a header) is
// enough. With no key configured (forgotten locally, say) we stay on the
// safe side and refuse access entirely.
var adminApiKey = app.Configuration["AdminApiKey"];
// A SEPARATE key for the endpoint that accepts externally collected products
// (see CollectorEndpoints). Not the admin key, because this value also lives
// on the development machine.
var ingestApiKey = app.Configuration["IngestApiKey"];

// For the "Back to site" link on confirm/unsubscribe pages and the product
// and site links in the digest: managed in one place so a domain change
// leaves nothing behind.
var frontendBaseUrl = app.Configuration["FrontendBaseUrl"] ?? "https://www.wheyproof.com";
var frontendImageSource = Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var frontendUri)
    && frontendUri.Scheme is "http" or "https"
        ? frontendUri.GetLeftPart(UriPartial.Authority)
        : null;

// Apply pending migrations on startup, so no manual migration step is needed
// on the host (a reasonable shortcut for a small, one-developer project).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IndirimTakip.Infrastructure.AppDbContext>();
    db.Database.Migrate();

    // Guide articles ship with the code; only slugs missing from the database
    // are added, so an article edited through the admin API is never overwritten.
    var seededArticles = IndirimTakip.Infrastructure.Articles.ArticleSeeder.SeedMissing(db);
    if (seededArticles > 0)
        app.Logger.LogInformation("Added {Count} guide articles from the repository.", seededArticles);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// UseForwardedHeaders MUST run before UseHsts: the HSTS middleware checks
// Request.IsHttps to decide whether to add its header. TLS ends at the proxy
// and we receive http, so until X-Forwarded-Proto is processed IsHttps is
// always false and the HSTS header is silently never added.
app.UseForwardedHeaders();

// PRODUCT IMAGES: our own resized copies.
//
// WHY NOT IN CADDY: the Caddyfile deliberately has NO `root`/`file_server`
// directive, and paths like `.git`, `.env` and `appsettings` returning 404 is
// exactly the result of that, not luck. Adding a file server there would
// weaken that property, and a broken Caddyfile takes the whole site down.
// Here the FileProvider is locked to one directory, directory listing is off,
// and escaping the path (../) is blocked by the framework.
//
// The directory is created up front: PhysicalFileProvider on a missing path
// FAILS AT STARTUP, which would stop the app from starting locally at all.
{
    var imageOptions = app.Services.GetRequiredService<ProductImageOptions>();
    Directory.CreateDirectory(imageOptions.StoragePath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.GetFullPath(imageOptions.StoragePath)),
        RequestPath = "/api/images",
        ServeUnknownFileTypes = false,
        OnPrepareResponse = ctx =>
        {
            // The file name is a hash of the content, so the content NEVER
            // changes: a new address means a new name. Immutable, long-lived
            // caching is therefore safe, and Cloudflare can keep it at the edge.
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        },
    });
}

// The layer that records security events. AFTER UseForwardedHeaders and
// BEFORE the exception handler: it needs the real client address and must
// also see the 500s the exception handler produces.
app.UseSecurityEventLogging();

// Global exception handling: an unexpected exception returns a generic JSON
// 500 and is logged; exception details never reach the client.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var error = exceptionFeature?.Error;

        // BadHttpRequestException is a client error thrown during
        // query/route/body binding (e.g. ?days=abc that won't parse as int).
        // It keeps its own status code (usually 400) instead of becoming a
        // generic 500, and isn't logged at ERROR level like a server fault.
        if (error is BadHttpRequestException badRequest)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = badRequest.StatusCode;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid request." });
            return;
        }

        if (error is { } ex)
            app.Logger.LogError(ex, "Unhandled error: {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "Something went wrong." });
    });
});

if (!app.Environment.IsDevelopment())
{
    // Production only, so HSTS doesn't break local http development.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicy);
// The output cache must come AFTER CORS, or cached responses could miss the
// CORS headers. After the rate limiter too, deliberately: requests served
// from the cache still count.
app.UseRateLimiter();
app.UseOutputCache();

// A browser-level defense layer. This API mostly returns JSON (these headers
// do nothing there), but /api/subscribe/confirm and /unsubscribe return real
// HTML pages clicked straight from email, so they shouldn't be open to
// clickjacking or MIME sniffing. The CSP allows inline styles (the info page
// uses them) but blocks scripts entirely. Images on the confirmation page come
// from the frontend domain; unless that origin is allowed, the browser blocks
// them even when they return 200. The configured value is parsed with Uri and
// only a safe http/https origin is added; the raw config text never goes into
// the header.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    var imageSources = frontendImageSource is null
        ? "'self'"
        : $"'self' {frontendImageSource}";
    context.Response.Headers.Append("Content-Security-Policy",
        $"default-src 'none'; style-src 'unsafe-inline'; img-src {imageSources}; base-uri 'none'; frame-ancestors 'none'");
    await next();
});

// Endpoints are grouped by topic in separate files (Endpoints/). This split
// is organization only: routes, cache policies, rate limits and filters are
// exactly the same.
app.MapAdminEndpoints(adminApiKey);
app.MapAdminSession(adminApiKey);
app.MapHealthEndpoints();
app.MapDealsEndpoints(PublicDataCachePolicy);
app.MapCouponEndpoints(PublicDataCachePolicy);
app.MapArticleEndpoints(PublicDataCachePolicy);
app.MapPriceHistoryEndpoints(PublicDataCachePolicy);
app.MapEngagementEndpoints(frontendBaseUrl);
app.MapStoreRedirectEndpoints();
app.MapSubscriptionEndpoints(frontendBaseUrl);


app.Run();
