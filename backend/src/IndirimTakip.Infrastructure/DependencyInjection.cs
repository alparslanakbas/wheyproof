using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using IndirimTakip.Infrastructure.Security;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure;

public static class DependencyInjection
{
    // Some sites (including ones behind Cloudflare) block requests without a User-Agent.
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        // Refreshes the public data cache after a scrape. The REAL implementation
        // lives in the Api project (tied to ASP.NET's output cache); this is only a
        // no-op fallback for environments without the Api (tests, console tools).
        // TryAdd, so it does NOT override the Api's registration.
        services.TryAddScoped<IPublicCacheRefresher, NullPublicCacheRefresher>();

        // The price summary (precomputed fields on Products) is refreshed after
        // every scrape with one set-based query.
        services.AddScoped<PriceSummaryRefresher>();

        // Store scrapers. Nearly every brand on the US shortlist runs on Shopify
        // and exposes the same public products.json endpoint, so one
        // configurable scraper replaces a class per brand. The store list
        // lives in ShopifyStores.All.
        services.AddHttpClient(ShopifyStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        foreach (var store in ShopifyStores.All)
        {
            services.AddScoped<IBrandScraper>(sp => new ShopifyStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ShopifyStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<ShopifyStoreScraper>>()));
        }

        // Notifies search engines of page changes (IndexNow: Bing and others).
        services.AddHttpClient<IndexNowClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        // Rating refresh isn't tied to a store-specific scraper: stores publish
        // ratings on the product page with the same schema.org fields, so one
        // generic client is enough (see ProductRatingRefreshService).
        services.AddHttpClient(ProductRatingRefreshService.RatingHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        });
        services.AddScoped<ProductRatingRefreshService>();

        services.AddScoped<ScrapeIngestionService>();
        services.AddScoped<ProductDetailBackfillService>();
        services.AddScoped<DealsQueryService>();
        services.AddScoped<PriceHistoryQueryService>();
        services.AddScoped<CouponService>();
        services.AddScoped<ArticleService>();
        services.AddHostedService<ScrapingBackgroundService>();
        // Sources that run once a day instead of every round (see IBrandScraper.DailyOnly).
        services.AddHostedService<DailyScrapingBackgroundService>();
        services.AddHostedService<DescriptionBackfillBackgroundService>();
        services.AddHostedService<RatingRefreshBackgroundService>();

        // Security event log. The counter lives in memory, so MemoryCache is
        // required; the TTL keeps the dictionary cleaning itself (see
        // SecurityEventRecorder).
        services.AddMemoryCache();
        // Singleton because it opens its own scope; see SecurityEventRecorder.
        services.AddSingleton<SecurityEventRecorder>();

        services.AddSingleton<AdminFailureRecorder>();

        // PRODUCT IMAGES. The options are registered as a POCO (not IOptions):
        // DealsQueryService is created per request and only reads the base
        // address, so an IOptions layer would buy nothing. The path must match
        // the static file route in Program.cs (/api/images).
        var imageOptions = new ProductImageOptions();
        configuration.GetSection("ProductImages").Bind(imageOptions);
        if (string.IsNullOrWhiteSpace(imageOptions.PublicBaseUrl))
            imageOptions.PublicBaseUrl = (configuration["PublicBaseUrl"] ?? string.Empty).TrimEnd('/') + "/api/images";
        services.AddSingleton(imageOptions);
        services.AddSingleton<ProductImageStore>();
        services.AddHostedService<ProductImageBackgroundService>();

        // Downloads from the sources' CDNs. The client identifies itself with a
        // bot User-Agent that names the site, so a store can see who is fetching
        // its images and how to reach us.
        services.AddHttpClient(ProductImageStore.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; WheyProofBot/1.0; +https://www.wheyproof.com)");
        });
        // NUTRITION LABELS. Read from the stores' label images with Claude's
        // vision model; see NutritionLabelService. Options as a POCO like the
        // image options. The key only ever goes into a request header.
        var labelOptions = new NutritionLabelOptions();
        configuration.GetSection("NutritionLabels").Bind(labelOptions);
        services.AddSingleton(labelOptions);
        services.AddScoped<NutritionLabelService>();
        services.AddHostedService<NutritionLabelBackgroundService>();
        services.AddHttpClient<NutritionLabelReader>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddHostedService<SecurityEventRetentionService>();

        services.AddHttpClient<IEmailSender, BrevoEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
        });
        services.AddScoped<EmailAddressValidator>();
        services.AddScoped<SubscriberService>();
        services.AddScoped<DigestService>();
        services.AddScoped<ProductWatchService>();
        services.AddScoped<ProductWatchNotifier>();
        services.AddScoped<FavoriteService>();
        services.AddHostedService<DigestBackgroundService>();

        return services;
    }
}
