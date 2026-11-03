namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// How long a 429 asks us to wait before trying again (RFC 9110 section 10.2.3, <c>Retry-After</c>).
/// </summary>
/// <remarks>
/// <b>WHY (2026-11-03).</b> Shopify's bot review asked us to wait for the time in the
/// <c>Retry-After</c> header rather than a fixed delay, and to keep honoring it after approval
/// (support ticket 71441774). Before this the retry waited a fixed minute.
/// </remarks>
public static class RetryAfter
{
    /// <summary>
    /// The longest wait a scraper sits through. A store asking for more is left until the next cycle,
    /// which comes hours later, instead of holding up the stores after it.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    /// <summary>The header's delay (seconds or a date), or <paramref name="fallback"/> when there is none.</summary>
    public static TimeSpan Of(HttpResponseMessage response, TimeSpan fallback, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - now : fallback);
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }
}
