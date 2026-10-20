namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// The "lowest since …" badge: is the current price the lowest we have seen since we started tracking the product?
/// </summary>
/// <remarks>
/// <b>The step above the "Lowest in 30 days" badge, not a separate definition.</b> On the Turkish site (2026-10-07)
/// the bare rule "current price is the lowest of the whole history and it used to cost more" picked products that
/// had merely returned to their usual price after a price rise, and new prices that hadn't moved for months. The
/// 30-day badge already rules both out with the usual price; this rule adds at least <see cref="MinimumHistoryDays"/>
/// days of history and the lowest of the whole history on top. Measured here on 2026-10-20: 54 of 77 US 30-day lows
/// and 18 of 24 UK ones pass, because tracking only started in September; the badge gets more selective as history
/// grows.
///
/// The badge says the date ("Lowest since Sep 13"): we can't say "all-time low", we don't know the price before
/// tracking started.
/// </remarks>
public static class TrackedLow
{
    /// <summary>The shortest history that lets us say "since we started tracking".</summary>
    public const int MinimumHistoryDays = 30;

    /// <returns>The start of tracking if the badge applies, otherwise null.</returns>
    public static DateTimeOffset? Since(
        bool atThirtyDayLow,
        decimal currentPrice,
        DateTimeOffset? trackedSince,
        decimal? lowestTrackedPrice,
        DateTimeOffset now) =>
        atThirtyDayLow
        && trackedSince is { } since && since <= now.AddDays(-MinimumHistoryDays)
        && lowestTrackedPrice is { } lowest && currentPrice <= lowest
            ? since
            : null;
}
