namespace IndirimTakip.Core.Entities;

/// <summary>
/// The moment a periodic background job LAST COMPLETED SUCCESSFULLY.
/// </summary>
/// <remarks>
/// <b>WHY A SEPARATE RECORD WAS NEEDED.</b> The detail backfill job answered "is
/// it my turn" with <c>MAX(Products.NutritionCheckedAt)</c>. The reasoning: only
/// this job writes that stamp, so the newest stamp is the last run time. It looks
/// right but MISSES ONE CASE: a run cut off halfway.
///
/// That happened in production: a run started, processed <b>a single product</b>
/// and was cancelled when a deploy recreated the container. That one stamp moved
/// MAX forward, so the next run was pushed back a full interval. On a day with
/// many deploys the job could keep being postponed while barely progressing.
///
/// <b>Why it's sneaky:</b> the nutrition series alarm doesn't catch it. That alarm
/// says "5+ products were checked but nutrition rose by 0"; here the checked
/// count doesn't rise either, so to the alarm nothing happened at all.
///
/// The stamp is now tied to the run's OWN completion: a run cut off halfway
/// doesn't update this record, and the schedule doesn't move.
///
/// <b>Why not in memory:</b> the period is measured in days. Kept in process
/// memory, every deploy would reset the counter and the period would never elapse;
/// the digest ran into exactly that (see DigestBackgroundService).
/// </remarks>
public class BackgroundJobRun
{
    public int Id { get; set; }

    /// <summary>The job's fixed name; unique.</summary>
    public required string JobName { get; set; }

    public DateTimeOffset LastCompletedAt { get; set; }
}

public static class BackgroundJobNames
{
    public const string DetailBackfill = "detail-backfill";

    // Scheduled through PersistedSchedule (Infrastructure): a deploy no longer
    // starts these, only an elapsed interval does.
    public const string ScrapeCycle = "scrape-cycle";
    public const string RatingRefresh = "rating-refresh";
}
