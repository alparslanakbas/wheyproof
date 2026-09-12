using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Security;

/// <summary>
/// Deletes security events once their retention period is over.
/// </summary>
/// <remarks>
/// <b>Why it's required.</b> Two reasons, each sufficient on its own:
/// (a) a table growing without limit turns the disk itself into a failure source
/// under attack traffic; (b) keeping a record containing personal data
/// indefinitely isn't defensible: a DEFINED retention period is part of what
/// makes the record legitimate.
///
/// <b>Why it keeps no state.</b> This codebase fell into the same trap twice: when
/// the timer itself holds the period, every deploy resets the process and the job
/// never runs (the digest and the detail backfill). That can't happen here,
/// because the deletion is ALREADY IDEMPOTENT: "delete the expired ones" every run
/// is enough, and a missed run is made up by the next one. So no stamp is needed.
/// </remarks>
public class SecurityEventRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SecurityEventRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = configuration.GetValue("SecurityLog:RetentionDays", 90);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
                var deleted = await db.SecurityEvents
                    .Where(x => x.OccurredAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    logger.LogInformation("Security event cleanup: {Count} records deleted (older than {Days} days).", deleted, retentionDays);

                // The admin failure log is cleaned with the SAME period. Keeping the
                // two records for different periods would contradict the single
                // period stated in the privacy policy; a second setting would also
                // mean a table growing without limit once forgotten.
                var adminDeleted = await db.AdminOperationFailures
                    .Where(x => x.OccurredAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (adminDeleted > 0)
                    logger.LogInformation("Admin failure cleanup: {Count} records deleted (older than {Days} days).", adminDeleted, retentionDays);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Security event cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
