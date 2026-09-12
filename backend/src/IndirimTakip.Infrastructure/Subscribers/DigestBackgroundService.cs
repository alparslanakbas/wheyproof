using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Subscribers;

public class DigestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DigestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Digest:Enabled", true))
        {
            logger.LogInformation("Scheduled digest sending is disabled (Digest:Enabled=false).");
            return;
        }

        // This timer NO LONGER decides the schedule; it is only how often to check.
        // The real decision (whose digest is due) is made every run from
        // Subscriber.LastDigestSentAt in the database.
        //
        // The timer used to hold the 7-day period itself, and that meant the digest
        // was never sent: every deploy/restart started the process from scratch, so
        // the 7-day period never elapsed even once. With the state in the database
        // a restart doesn't affect the schedule, and for the same reason checking
        // right at startup is safe (sending still requires 7 days per subscriber, so
        // a deploy doesn't send mail again).
        var checkIntervalHours = configuration.GetValue("Digest:CheckIntervalHours", 6);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(checkIntervalHours));

        do
        {
            await SendDigestAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDigestAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var digest = scope.ServiceProvider.GetRequiredService<DigestService>();
        var baseUrl = configuration["PublicBaseUrl"] ?? "http://localhost:5156";

        try
        {
            var result = await digest.SendDigestAsync(baseUrl, cancellationToken);
            if (result.SubscriberCount > 0 || result.PendingCount > 0)
            {
                logger.LogInformation(
                    "Scheduled digest: {DealCount} products, sent to {SubscriberCount} subscribers, {PendingCount} left for the next run.",
                    result.DealCount, result.SubscriberCount, result.PendingCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled digest sending failed.");
        }
    }
}
