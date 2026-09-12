using IndirimTakip.Core.Caching;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Scrapes sources marked <see cref="IBrandScraper.DailyOnly"/> once a day at a
/// fixed time.
///
/// Why a separate service: the regular round runs every 6 hours and starts when
/// the application starts, so it can't be pinned to a time of day. Daily sources
/// needed a fixed time: scraping at the day boundary makes it easy to represent a
/// day's price with one measurement belonging to that day.
///
/// The split exists because of cost: these sources render the product list in the
/// browser, so every product needs its own request. Pulling 900+ products every
/// 6 hours would mean thousands of requests a day to the other server and would
/// seriously raise the risk of being blocked.
///
/// <b>NOTE:</b> no US store is DailyOnly yet, so this service currently has
/// nothing to run.
/// </summary>
public class DailyScrapingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DailyScrapingBackgroundService> logger) : BackgroundService
{
    // 21:00 UTC, inherited from the Turkish site, where it was midnight local time
    // (Turkey is a fixed UTC+3 with no daylight saving). It is NOT midnight in the
    // US market. When a daily source is added, move this to the market's time
    // zone; a fixed UTC hour won't do there, because America/New_York observes
    // daylight saving time.
    private const int RunAtUtcHour = 21;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scraping:Enabled", true))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(DateTimeOffset.UtcNow);
            logger.LogInformation(
                "Daily scrape runs in {Delay} ({Hour}:00 UTC).",
                delay, RunAtUtcHour);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Time left until the next 21:00 UTC. An exact hit moves to the next day;
    /// otherwise the scrape could fire again with zero delay right after finishing.
    /// </summary>
    internal static TimeSpan NextRunDelay(DateTimeOffset now)
    {
        var todaysRun = new DateTimeOffset(now.Year, now.Month, now.Day, RunAtUtcHour, 0, 0, TimeSpan.Zero);
        var target = now < todaysRun ? todaysRun : todaysRun.AddDays(1);
        return target - now;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scrapers = scope.ServiceProvider.GetServices<IBrandScraper>()
            .Where(s => s.DailyOnly)
            .ToList();

        if (scrapers.Count == 0)
            return;

        var ingestion = scope.ServiceProvider.GetRequiredService<ScrapeIngestionService>();
        logger.LogInformation("Daily scrape started ({Count} sources).", scrapers.Count);

        foreach (var scraper in scrapers)
        {
            try
            {
                var count = await ingestion.IngestAsync(scraper, cancellationToken);
                logger.LogInformation("{Brand}: {Count} products scraped (daily).", scraper.BrandName, count);
            }
            catch (Exception ex)
            {
                // One source failing must not stop the others.
                logger.LogError(ex, "Error while scraping {Brand} (daily).", scraper.BrandName);
            }
        }

        // The price summary goes FIRST: cache warming reads those fields, and the
        // reverse order would cache the old summary.
        await scope.ServiceProvider.GetRequiredService<PriceSummaryRefresher>()
            .RefreshAsync(cancellationToken);

        await scope.ServiceProvider.GetRequiredService<IPublicCacheRefresher>()
            .RefreshAsync(cancellationToken);

        logger.LogInformation("Daily scrape finished.");
    }
}
