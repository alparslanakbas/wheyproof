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
    // Every scraping client is the registered bot (see WebBotAuth); WebBotAuth:UserAgent overrides
    // the name. Until 2026-11-03 they sent a browser User-Agent; Shopify's review asked for none,
    // and every other store answered the bot exactly as it answered the browser (measured that day:
    // Huel, Form, Bulk, Myprotein, 365Rider). Each also checks robots.txt (see RobotsTxtHandler).
    private const string BotUserAgent = "Mozilla/5.0 (compatible; wheyproofbot/1.0; +https://www.wheyproof.com/bot)";

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
        // No home tunnel any more (2026-11-03): it repeated a 429 at once from another address, the
        // opposite of what Shopify's review asked (wait for Retry-After); see ShopifyStoreScraper.
        // Shopify requests carry the registered bot's User-Agent and are signed (see WebBotAuth);
        // key from .env, without one they go out unsigned with the same User-Agent.
        var botUserAgent = configuration["WebBotAuth:UserAgent"] is { Length: > 0 } configured ? configured.Trim() : BotUserAgent;
        services.AddSingleton(_ => WebBotAuth.Create(configuration["WebBotAuth:PrivateKey"],
            configuration["WebBotAuth:SignatureAgent"], botUserAgent, TimeProvider.System,
            configuration.GetValue("WebBotAuth:SignRequests", true)));
        // Each Shopify host's robots.txt, read once a day and checked before every request (see RobotsRules).
        services.AddSingleton(new RobotsTxtCache(TimeProvider.System));
        services.AddHttpClient(ShopifyStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
            // robots.txt first: a disallowed request isn't signed or sent at all.
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
        foreach (var store in ShopifyStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new ShopifyStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ShopifyStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<ShopifyStoreScraper>>()));
        }

        // Huel (both editions): its own product pages since it stopped publishing to Shopify's
        // storefront (see HuelScraper). Redirects off: a page that redirects was served for another
        // country and must fail rather than be read. Cookies off, so the locale cookie the scraper
        // sets goes out as written. Compression on: a page is about 1 MB unpacked.
        services.AddHttpClient(Scraping.Huel.HuelScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
            client.Timeout = TimeSpan.FromSeconds(60);
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = PublicNetworkConnection.CreateHandler();
            handler.AllowAutoRedirect = false;
            handler.UseCookies = false;
            handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
            return handler;
        })
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
        services.AddScoped<IBrandScraper>(sp => new Scraping.Huel.HuelScraper(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.Huel.HuelScraper.HttpClientName),
            Scraping.Huel.HuelScraper.ForMarket(market),
            sp.GetRequiredService<ILogger<Scraping.Huel.HuelScraper>>()));

        // WooCommerce stores. Same idea as the Shopify list: one configurable
        // scraper reads the public Store API, the stores live in WooStores.All.
        services.AddHttpClient(Scraping.Woo.WooStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
        foreach (var store in Scraping.Magento.MagentoStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new Scraping.Magento.MagentoStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.Magento.MagentoStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<Scraping.Magento.MagentoStoreScraper>>()));
        }

        // PrestaShop stores (none since 365Rider was dropped, see PrestaShopStores): one
        // configurable scraper reads the category listing, the stores live in PrestaShopStores.All.
        // Its own handler, still the public-network one: cookies stay on (the
        // store keeps language and currency in the session) and redirects are
        // off, because a listing page answering 302 means the session was lost
        // and must fail rather than be read (see PrestaShopStoreScraper).
        services.AddHttpClient(Scraping.PrestaShop.PrestaShopStoreScraper.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = PublicNetworkConnection.CreateHandler();
            handler.AllowAutoRedirect = false;
            return handler;
        })
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
        foreach (var store in Scraping.PrestaShop.PrestaShopStores.ForMarket(market))
        {
            services.AddScoped<IBrandScraper>(sp => new Scraping.PrestaShop.PrestaShopStoreScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.PrestaShop.PrestaShopStoreScraper.HttpClientName),
                store,
                sp.GetRequiredService<ILogger<Scraping.PrestaShop.PrestaShopStoreScraper>>()));
        }

        // Myprotein (UK only): THG's own platform, one page per product, read once a
        // day (see MyproteinScraper). Still the public-network handler, with
        // compression on: a page is about 1 MB unpacked and 120 kB packed. Redirects
        // stay on, because listings link older slugs that redirect to the product's
        // canonical address.
        if (market == SiteMarket.Uk)
        {
            services.AddHttpClient(Scraping.Myprotein.MyproteinScraper.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
                client.Timeout = TimeSpan.FromSeconds(60);
            }).ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = PublicNetworkConnection.CreateHandler();
                handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
                return handler;
            })
                .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                    sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
                .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
            services.AddScoped<IBrandScraper>(sp => new Scraping.Myprotein.MyproteinScraper(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(Scraping.Myprotein.MyproteinScraper.HttpClientName),
                sp.GetRequiredService<ILogger<Scraping.Myprotein.MyproteinScraper>>()));
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
        })
            .AddHttpMessageHandler(sp => new RobotsTxtHandler(sp.GetRequiredService<RobotsTxtCache>(),
                sp.GetRequiredService<ILogger<RobotsTxtHandler>>()))
            .AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
        services.AddScoped<ProductRatingRefreshService>();

        services.AddScoped<ScrapeIngestionService>();
        services.AddScoped<ProductDetailBackfillService>();
        services.AddScoped<ManualProductDataService>();
        services.AddScoped<DealsQueryService>();
        services.AddScoped<CatalogStatsQueryService>();
        services.AddScoped<ValuePicksQueryService>();
        services.AddScoped<CampaignDiscountService>();
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

        // Downloads from the sources' CDNs (most of them Shopify's). The client is the
        // registered bot, signed like the catalog requests, so a store can see who is
        // fetching its images and how to reach us.
        services.AddHttpClient(ProductImageStore.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
        }).AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(botUserAgent);
        }).AddHttpMessageHandler(sp => new WebBotAuthHandler(sp.GetRequiredService<WebBotAuth>()));
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
