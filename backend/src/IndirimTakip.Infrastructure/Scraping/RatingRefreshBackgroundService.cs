using IndirimTakip.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

// Regularly refreshes the star averages on the stores' sites.
//
// Unlike the description backfill: there an interval check ("is it my turn")
// exists because the work, once done, needn't be repeated. Ratings keep changing;
// here every run refreshes the products checked longest ago, so the work never
// "finishes". The order comes from the RatingCheckedAt stamp, so scheduling lives
// in the DATABASE, not process memory, and deploys don't reset it (the digest ran
// into exactly that bug, see DigestBackgroundService). The TIMING does too now:
// it used to run at every start, so each deploy sent a round of product-page
// requests to every store (see PersistedSchedule).
public class RatingRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RatingRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("RatingRefresh:Enabled", true))
        {
            logger.LogInformation("Rating refresh is disabled (RatingRefresh:Enabled=false).");
            return;
        }

        var intervalHours = configuration.GetValue("RatingRefresh:IntervalHours", 6);
        await PersistedSchedule.RunAsync(
            scopeFactory, BackgroundJobNames.RatingRefresh, TimeSpan.FromHours(intervalHours), logger,
            async (services, cancellationToken) =>
            {
                try
                {
                    await services.GetRequiredService<ProductRatingRefreshService>()
                        .RefreshAsync(cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A failed run still counts as a run: retrying at once would
                    // hit the same stores again. Shutdown propagates instead, so
                    // an interrupted run keeps the old stamp.
                    logger.LogError(ex, "Error during rating refresh.");
                }
            },
            stoppingToken);
    }
}
