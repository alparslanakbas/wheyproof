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
// into exactly that bug, see DigestBackgroundService).
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
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        do
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ProductRatingRefreshService>();

            try
            {
                await service.RefreshAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during rating refresh.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
