using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Entities;
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
/// Scheduled in the database (see PersistedSchedule.RunDailyAsync): a scrape cut
/// off by a deploy runs again at the next start instead of waiting a day.
/// Myprotein (UK) is the only daily source so far; the US site has none.
/// </summary>
public class DailyScrapingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DailyScrapingBackgroundService> logger) : BackgroundService
{
    // 21:00 UTC, inherited from the Turkish site, where it is midnight local time.
    // The hour means nothing in the US or UK market; what matters is one scrape
    // per UTC day, which is also the day unit of the usual-price rule
    // (PriceSummaryRefresher). A local hour with daylight saving would break that.
    private const int RunAtUtcHour = 21;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        configuration.GetValue("Scraping:Enabled", true)
            ? PersistedSchedule.RunDailyAsync(
                scopeFactory, BackgroundJobNames.DailyScrape, RunAtUtcHour, logger, RunAsync, stoppingToken)
            : Task.CompletedTask;

    private async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var scrapers = services.GetServices<IBrandScraper>()
            .Where(s => s.DailyOnly)
            .ToList();

        if (scrapers.Count == 0)
            return;

        var ingestion = services.GetRequiredService<ScrapeIngestionService>();
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
        await services.GetRequiredService<PriceSummaryRefresher>()
            .RefreshAsync(cancellationToken);

        await services.GetRequiredService<IPublicCacheRefresher>()
            .RefreshAsync(cancellationToken);

        logger.LogInformation("Daily scrape finished.");
    }
}
