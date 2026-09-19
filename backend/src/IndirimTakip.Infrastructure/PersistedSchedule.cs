using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure;

/// <summary>
/// Runs a periodic job on a schedule kept in the DATABASE: the job's own
/// completion record (<see cref="BackgroundJobRun"/>) decides when it is due.
/// </summary>
/// <remarks>
/// <b>WHY.</b> The scrape and the rating refresh were written as
/// <c>do { run } while (await timer.WaitForNextTickAsync())</c>: a run at every
/// start, then a timer held in process memory. Every deploy recreates the
/// container, so every deploy started a full scrape of every store and reset the
/// clock. Deploys on the same day stacked those cycles up: three in one hour made
/// Kaged and Nutricost answer 429 (measured 2026-09-18), and pushes had to be
/// held back an hour to keep cycles apart.
///
/// With the completion time in the database, a start only runs the job if its
/// interval has passed since the last COMPLETED run; otherwise it waits out the
/// rest. The stamp is written only when a run finishes, so a run cut off by a
/// deploy leaves the old stamp in place and the next start runs it again at
/// once: an interrupted scrape saves nothing, and that day's prices mustn't wait
/// a whole interval.
/// </remarks>
public static class PersistedSchedule
{
    /// <summary>How long until a job is due; zero when it is due now.</summary>
    internal static TimeSpan TimeUntilDue(DateTimeOffset? lastCompleted, TimeSpan interval, DateTimeOffset now)
    {
        if (lastCompleted is null)
            return TimeSpan.Zero;

        var remaining = lastCompleted.Value + interval - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Waits until the job is due, runs it, records the completion, and repeats
    /// until the host stops. <paramref name="run"/> is called with a fresh scope.
    /// </summary>
    public static async Task RunAsync(
        IServiceScopeFactory scopeFactory,
        string jobName,
        TimeSpan interval,
        ILogger logger,
        Func<IServiceProvider, CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lastCompleted = await ReadLastCompletedAsync(scopeFactory, jobName, stoppingToken);
                var wait = TimeUntilDue(lastCompleted, interval, DateTimeOffset.UtcNow);
                if (wait > TimeSpan.Zero)
                {
                    logger.LogInformation(
                        "{Job}: last completed {LastCompleted:u}, next run in {Wait}.",
                        jobName, lastCompleted, wait);
                    await Task.Delay(wait, stoppingToken);
                }

                using (var scope = scopeFactory.CreateScope())
                {
                    await run(scope.ServiceProvider, stoppingToken);
                }

                // Only reached when the run returned: a cancelled run throws past
                // this line and keeps the previous stamp.
                await MarkCompletedAsync(scopeFactory, jobName, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An exception escaping a BackgroundService stops the whole host by
                // default, and a restart would begin this job again from scratch.
                // A database hiccup while reading or writing the stamp is logged and
                // retried in a minute instead.
                logger.LogError(ex, "{Job}: scheduling failed; retrying in a minute.", jobName);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private static async Task<DateTimeOffset?> ReadLastCompletedAsync(
        IServiceScopeFactory scopeFactory, string jobName, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BackgroundJobRuns
            .Where(j => j.JobName == jobName)
            .Select(j => (DateTimeOffset?)j.LastCompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task MarkCompletedAsync(
        IServiceScopeFactory scopeFactory, string jobName, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.BackgroundJobRuns.FirstOrDefaultAsync(j => j.JobName == jobName, cancellationToken);
        if (record is null)
            db.BackgroundJobRuns.Add(new BackgroundJobRun { JobName = jobName, LastCompletedAt = DateTimeOffset.UtcNow });
        else
            record.LastCompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
