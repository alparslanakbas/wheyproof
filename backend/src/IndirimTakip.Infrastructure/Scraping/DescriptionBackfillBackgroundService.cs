using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

public class DescriptionBackfillBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DescriptionBackfillBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("DescriptionBackfill:Enabled", true))
        {
            logger.LogInformation("Description backfill is disabled (DescriptionBackfill:Enabled=false).");
            return;
        }

        // The default went from 7 days to 2: together with the per-run product
        // count, it closes an accumulated gap in about 9 days instead of 11 weeks.
        // Being configurable is deliberate: if a source complains it can be slowed
        // down without waiting for a deploy.
        var intervalDays = configuration.GetValue("DescriptionBackfill:IntervalDays", 2);

        // The timer no longer holds the period itself, only how often to check;
        // "is it time" is decided from the last completion stamp in the database
        // (ProductDetailBackfillService.IsDueAsync).
        //
        // The timer used to hold the period, and that meant the job never ran: every
        // deploy/restart started the process from scratch, so the 7-day period never
        // elapsed even once (the digest hit the same bug, see
        // DigestBackgroundService). With the state in the database, checking right
        // at startup is safe too: if the interval hasn't passed, no request goes out
        // to the stores.
        var checkIntervalHours = configuration.GetValue("DescriptionBackfill:CheckIntervalHours", 6);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(checkIntervalHours));

        do
        {
            using var scope = scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<ProductDetailBackfillService>();

            try
            {
                if (!await backfill.IsDueAsync(intervalDays, stoppingToken))
                    continue;

                var updated = await backfill.BackfillAsync(stoppingToken);
                logger.LogInformation("Description backfill ran: {Count} products updated.", updated);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during description backfill.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
