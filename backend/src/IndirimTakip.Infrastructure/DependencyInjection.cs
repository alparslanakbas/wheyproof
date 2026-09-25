using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Catalog;
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

        // Every HTTP client connects to public addresses only (SSRF protection,
        // see PublicNetworkConnection). Set as the default so a new scraper
        // can't forget it; a client that goes internal on purpose has to pick
        // its own handler explicitly.
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(PublicNetworkConnection.CreateHandler));

        // Refreshes the public data cache after a scrape. The REAL implementation
        // lives in the Api project (tied to ASP.NET's output cache); this is only a
        // no-op fallback for environments without the Api (tests, console tools).
        // TryAdd, so it does NOT override the Api's registration.
        services.TryAddScoped<IPublicCacheRefresher, NullPublicCacheRefresher>();

        // The price summary (precomputed fields on Products) is refreshed after
        // every scrape with one set-based query.
        services.AddScoped<PriceSummaryRefresher>();

        // The edition this instance serves (US site or UK section). It decides
        // which stores are registered below and the currency they must answer in.
        var market = SiteMarket.FromConfiguration(configuration);
        services.AddSingleton(market);
        Subscribers.EmailTemplate.PriceCulture = market.Culture;

        // Store scrapers. Nearly every brand on the US shortlist runs on Shopify
        // and exposes the same public products.json endpoint, so one
        // configurable scraper replaces a class per brand. The store list
        // lives in ShopifyStores.All.
        services.AddHttpClient(ShopifyStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        foreach (var store in ShopifyStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new ShopifyStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ShopifyStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<ShopifyStoreScraper>>()));
        }

        // WooCommerce stores. Same idea as the Shopify list: one configurable
        // scraper reads the public Store API, the stores live in WooStores.All.
        services.AddHttpClient(Scraping.Woo.WooStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        foreach (var store in Scraping.Woo.WooStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new Scraping.Woo.WooStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.Woo.WooStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<Scraping.Woo.WooStoreScraper>>()));
        }

        // Magento stores (Bulk in the UK section): one configurable scraper reads
        // the public storefront GraphQL, the stores live in MagentoStores.All.
        services.AddHttpClient(Scraping.Magento.MagentoStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        foreach (var store in Scraping.Magento.MagentoStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new Scraping.Magento.MagentoStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.Magento.MagentoStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<Scraping.Magento.MagentoStoreScraper>>()));
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
        services.AddScoped<ManualProductDataService>();
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
        // The free engine downloads label images itself; same bot identity as
        // the product image downloads.
        services.AddHttpClient<TesseractLabelReader>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; WheyProofBot/1.0; +https://www.wheyproof.com)");
        });
        services.AddScoped<INutritionLabelReader>(sp =>
            labelOptions.Engine.Equals("claude", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<NutritionLabelReader>()
                : sp.GetRequiredService<TesseractLabelReader>());

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
