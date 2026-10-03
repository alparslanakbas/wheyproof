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
///
/// <b>DAILY JOBS (2026-10-04).</b> The daily scrape runs at a fixed hour and used
/// to wait for the next 21:00 in process memory: a deploy that cut it off meant
/// no scrape until the next day (on the Turkish site a dealer's nightly data was
/// lost that way). <see cref="RunDailyAsync"/> uses the same stamp: if the last
/// completion is before the latest scheduled hour, the job runs at once.
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
    /// How long until a job that runs every day at <paramref name="runAtUtcHour"/> is
    /// due. Zero if it hasn't completed since the latest scheduled hour (a deploy cut
    /// it off, the server was down). With no record (the job's first start on this
    /// schedule), the next scheduled hour: a midday deploy shouldn't start the
    /// nightly scrape.
    /// </summary>
    internal static TimeSpan TimeUntilDailyDue(DateTimeOffset? lastCompleted, int runAtUtcHour, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date.AddHours(runAtUtcHour), TimeSpan.Zero);
        var latestScheduled = now >= today ? today : today.AddDays(-1);
        if (lastCompleted is not null && lastCompleted < latestScheduled)
            return TimeSpan.Zero;

        return latestScheduled.AddDays(1) - now;
    }

    /// <summary>
    /// Waits until the job is due, runs it, records the completion, and repeats
    /// until the host stops. <paramref name="run"/> is called with a fresh scope.
    /// </summary>
    public static Task RunAsync(
        IServiceScopeFactory scopeFactory,
        string jobName,
        TimeSpan interval,
        ILogger logger,
        Func<IServiceProvider, CancellationToken, Task> run,
        CancellationToken stoppingToken) =>
        RunCoreAsync(scopeFactory, jobName, (last, now) => TimeUntilDue(last, interval, now), logger, run, stoppingToken);

    /// <summary>Like <see cref="RunAsync"/>, but every day at a fixed hour (UTC).</summary>
    public static Task RunDailyAsync(
        IServiceScopeFactory scopeFactory,
        string jobName,
        int runAtUtcHour,
        ILogger logger,
        Func<IServiceProvider, CancellationToken, Task> run,
        CancellationToken stoppingToken) =>
        RunCoreAsync(scopeFactory, jobName, (last, now) => TimeUntilDailyDue(last, runAtUtcHour, now), logger, run, stoppingToken);

    private static async Task RunCoreAsync(
        IServiceScopeFactory scopeFactory,
        string jobName,
        Func<DateTimeOffset?, DateTimeOffset, TimeSpan> timeUntilDue,
        ILogger logger,
        Func<IServiceProvider, CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lastCompleted = await ReadLastCompletedAsync(scopeFactory, jobName, stoppingToken);
                var wait = timeUntilDue(lastCompleted, DateTimeOffset.UtcNow);
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
