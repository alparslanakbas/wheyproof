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
}
