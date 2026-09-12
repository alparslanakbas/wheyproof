using IndirimTakip.Core.Caching;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

public class ScrapingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ScrapingBackgroundService> logger) : BackgroundService
{
    // Courtesy delay between stores: not to hammer their sites and to lower the risk of an IP block.
    private static readonly TimeSpan DelayBetweenBrands = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scraping:Enabled", true))
        {
            logger.LogInformation("Scheduled scraping is disabled (Scraping:Enabled=false).");
            return;
        }

        var intervalHours = configuration.GetValue("Scraping:IntervalHours", 6);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        do
        {
            await RunScrapeCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunScrapeCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        // Sources marked DailyOnly are OUTSIDE this round:
        // DailyScrapingBackgroundService runs them once a day.
        var scrapers = scope.ServiceProvider.GetServices<IBrandScraper>()
            .Where(s => !s.DailyOnly)
            .ToList();
        var ingestion = scope.ServiceProvider.GetRequiredService<ScrapeIngestionService>();

        logger.LogInformation("Scrape cycle started ({Count} stores).", scrapers.Count);

        foreach (var scraper in scrapers)
        {
            try
            {
                var count = await ingestion.IngestAsync(scraper, cancellationToken);
                logger.LogInformation("{Brand}: {Count} products scraped.", scraper.BrandName, count);
            }
            catch (Exception ex)
            {
                // One store failing must not stop the others.
                logger.LogError(ex, "Error while scraping {Brand}.", scraper.BrandName);
            }

            await Task.Delay(DelayBetweenBrands, cancellationToken);
        }

        // The data changed: drop the cache and warm the hot endpoints again.
        // Otherwise the next visitor hits a cold cache (measured on the Turkish
        // site: home page 6.0 s cold, 0.26 s warm).
        // The price summary goes FIRST: cache warming reads those fields, and the
        // reverse order would cache the old summary.
        await scope.ServiceProvider.GetRequiredService<PriceSummaryRefresher>()
            .RefreshAsync(cancellationToken);

        await scope.ServiceProvider.GetRequiredService<IPublicCacheRefresher>()
            .RefreshAsync(cancellationToken);

        logger.LogInformation("Scrape cycle finished.");
    }
}
