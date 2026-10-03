namespace IndirimTakip.Infrastructure.Tests;

// A deploy restarts the container. The scrape used to run at every start, so
// every deploy began a full cycle of every store; now a start runs it only if
// the interval has passed since the last COMPLETED run.
public class PersistedScheduleTests
{
    private static readonly TimeSpan SixHours = TimeSpan.FromHours(6);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_job_that_never_ran_is_due_now() =>
        Assert.Equal(TimeSpan.Zero, PersistedSchedule.TimeUntilDue(null, SixHours, Now));

    // THE DEPLOY CASE: the last cycle finished an hour ago, so a restart waits
    // five hours instead of scraping every store again.
    [Fact]
    public void A_restart_soon_after_a_completed_run_waits_out_the_interval() =>
        Assert.Equal(TimeSpan.FromHours(5), PersistedSchedule.TimeUntilDue(Now.AddHours(-1), SixHours, Now));

    [Fact]
    public void An_overdue_job_runs_at_once() =>
        Assert.Equal(TimeSpan.Zero, PersistedSchedule.TimeUntilDue(Now.AddHours(-9), SixHours, Now));

    [Fact]
    public void Exactly_one_interval_later_is_due() =>
        Assert.Equal(TimeSpan.Zero, PersistedSchedule.TimeUntilDue(Now - SixHours, SixHours, Now));

    // DAILY JOB (21:00 UTC). It's 16:00 UTC; yesterday's run finished at 21:50.
    private const int DailyHour = 21;
    private static readonly DateTimeOffset Afternoon = new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset YesterdaysRun = new(2026, 9, 18, 21, 50, 0, TimeSpan.Zero);

    [Fact]
    public void A_daily_job_started_before_its_hour_waits_for_it() =>
        Assert.Equal(TimeSpan.FromHours(5), PersistedSchedule.TimeUntilDailyDue(YesterdaysRun, DailyHour, Afternoon));

    // THE DEPLOY CASE: today's run started at 21:00, a deploy at 21:30 cut it off and
    // the stamp stayed at yesterday's. The start runs it at once instead of tomorrow.
    [Fact]
    public void A_daily_run_cut_off_by_a_deploy_runs_again_at_once() =>
        Assert.Equal(TimeSpan.Zero, PersistedSchedule.TimeUntilDailyDue(YesterdaysRun, DailyHour, Afternoon.AddHours(5.5)));

    [Fact]
    public void A_missed_day_is_caught_up() =>
        Assert.Equal(TimeSpan.Zero, PersistedSchedule.TimeUntilDailyDue(YesterdaysRun.AddDays(-1), DailyHour, Afternoon));

    // Today's run finished at 21:55; a deploy at 22:10 doesn't start it again.
    [Fact]
    public void A_completed_daily_job_waits_for_the_next_day() =>
        Assert.Equal(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(50),
            PersistedSchedule.TimeUntilDailyDue(Afternoon.AddHours(5).AddMinutes(55), DailyHour, Afternoon.AddHours(6).AddMinutes(10)));

    // A job with no record (first start on this schedule) doesn't start the nightly scrape midday.
    [Fact]
    public void A_daily_job_with_no_record_waits_for_its_hour() =>
        Assert.Equal(TimeSpan.FromHours(5), PersistedSchedule.TimeUntilDailyDue(null, DailyHour, Afternoon));
}
