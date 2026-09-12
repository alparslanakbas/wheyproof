using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
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
    // Bazı siteler (Cloudflare arkasındakiler dahil) User-Agent'sız istekleri engelliyor.
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        // Tarama bitince genel veri önbelleğini tazeleyen bağımlılık. GERÇEK
        // uygulama Api projesinde (ASP.NET'in çıktı önbelleğine bağlı);
        // buradaki yalnızca Api olmadan çalışan ortamlar (testler, konsol
        // araçları) için boş yedek. TryAdd olduğu için Api'nin kaydını EZMEZ.
        services.TryAddScoped<IPublicCacheRefresher, NullPublicCacheRefresher>();

        // Fiyat özeti (Products üzerindeki önceden hesaplanmış alanlar) her
        // taramadan sonra tek küme sorgusuyla tazeleniyor.
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

        // Arama motorlarına sayfa değişikliği bildirimi (Bing/Yandex/Seznam).
        services.AddHttpClient<IndexNowClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        // Puan tazeleme, markaya özel bir scraper'a bağlı değil: tüm markalar
        // puanı ürün sayfasında aynı schema.org alanlarıyla verdiği için tek
        // bir genel istemci yetiyor (bkz. ProductRatingRefreshService).
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
        // Günde bir kez, 00:00 Türkiye saatinde çalışan kaynaklar (bkz. IBrandScraper.DailyOnly).
        services.AddHostedService<DailyScrapingBackgroundService>();
        services.AddHostedService<DescriptionBackfillBackgroundService>();
        services.AddHostedService<RatingRefreshBackgroundService>();

        // Guvenlik olayi kaydi. Sayac bellekte tutuldugu icin MemoryCache sart;
        // TTL sayesinde sozluk kendi kendini temizliyor (bkz. SecurityEventRecorder).
        services.AddMemoryCache();
        // Kendi kapsamini actigi icin singleton; bkz. SecurityEventRecorder.
        services.AddSingleton<SecurityEventRecorder>();

        services.AddSingleton<AdminFailureRecorder>();

        // PRODUCT IMAGES. The options are registered as a POCO (not IOptions):
        // DealsQueryService is created per request and only reads the base
        // address, so an IOptions layer would buy nothing. The path must match
        // the static file route in Program.cs (/api/images).
        var imageOptions = new ProductImageOptions();
        configuration.GetSection("ProductImages").Bind(imageOptions);
        if (string.IsNullOrWhiteSpace(imageOptions.TabanAdres))
            imageOptions.TabanAdres = (configuration["PublicBaseUrl"] ?? string.Empty).TrimEnd('/') + "/api/images";
        services.AddSingleton(imageOptions);
        services.AddSingleton<ProductImageStore>();
        services.AddHostedService<ProductImageBackgroundService>();

        // Kaynakların CDN'lerinden indiriyor. Tarayıcı benzeri bir kimlik
        // veriliyor: bazı kaynaklar UA'sız isteklere görsel vermiyor
        // (Supplementler ve Renovafood'da ölçülmüş bir davranış).
        services.AddHttpClient(ProductImageStore.HttpClientAdi, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; ProteinAvcisiBot/1.0; +https://www.proteinavcisi.com.tr)");
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
