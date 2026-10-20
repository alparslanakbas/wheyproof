using IndirimTakip.Infrastructure.Deals;
using Xunit;

namespace IndirimTakip.Infrastructure.Tests;

// The "lowest since …" badge rule. The badge is a claim ("lowest since we started tracking"); most tests check that
// the badge does NOT appear where the claim would be false.
public class TrackedLowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FortyDaysAgo = Now.AddDays(-40);

    [Fact]
    public void A_product_at_its_30_day_low_and_the_lowest_of_its_whole_history_gets_the_tracking_start() =>
        Assert.Equal(FortyDaysAgo, TrackedLow.Since(true, 90m, FortyDaysAgo, 90m, Now));

    [Fact]
    public void A_product_that_once_sold_for_less_gets_no_badge() =>
        Assert.Null(TrackedLow.Since(true, 90m, FortyDaysAgo, 85m, Now));

    // Without the 30-day badge (the price never moved, or it is back at its usual price after a rise) there is no
    // badge either: equalling the lowest of the whole history isn't a low on its own.
    [Fact]
    public void Without_the_30_day_badge_there_is_no_badge_even_at_the_lowest_of_the_whole_history() =>
        Assert.Null(TrackedLow.Since(false, 90m, FortyDaysAgo, 90m, Now));

    [Fact]
    public void Under_30_days_of_history_no_badge_at_exactly_30_days_a_badge()
    {
        Assert.Null(TrackedLow.Since(true, 90m, Now.AddDays(-29), 90m, Now));
        Assert.NotNull(TrackedLow.Since(true, 90m, Now.AddDays(-TrackedLow.MinimumHistoryDays), 90m, Now));
    }

    // Before the first summary after the migration the fields are empty: no badge, no error.
    [Fact]
    public void Empty_summary_fields_give_no_badge()
    {
        Assert.Null(TrackedLow.Since(true, 90m, null, 90m, Now));
        Assert.Null(TrackedLow.Since(true, 90m, FortyDaysAgo, null, Now));
    }

    // On the product page the current price is live and the summary can be one scrape behind: a new low shows
    // before the summary catches up.
    [Fact]
    public void A_live_price_below_the_summary_low_gets_the_badge() =>
        Assert.Equal(FortyDaysAgo, TrackedLow.Since(true, 88m, FortyDaysAgo, 90m, Now));
}
