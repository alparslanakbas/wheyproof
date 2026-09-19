using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure;

/// <summary>
/// The country edition a running instance serves. The US site and the UK
/// section (wheyproof.com/uk) are the SAME application run twice, each with its
/// own database; this value is what differs between the two processes.
/// </summary>
/// <remarks>
/// One setting, <c>Market:Code</c>, picks the edition. Currency and culture
/// come from the code rather than from separate settings, so an instance can't
/// end up with UK stores and dollar formatting: that would publish plausible
/// numbers under the wrong symbol, with no error anywhere.
///
/// Why two processes and not a market column: every query, cache key, price
/// summary and sitemap would need the extra dimension, and DealsQueryService is
/// the class that took the Turkish site down twice. Two instances need no
/// change to any of it.
/// </remarks>
public sealed record SiteMarket(string Code, string Currency, string CultureName)
{
    public static readonly SiteMarket Us = new("US", "USD", "en-US");
    public static readonly SiteMarket Uk = new("UK", "GBP", "en-GB");

    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);

    /// <summary>
    /// The configured market. Missing means US, the site that existed first.
    /// An unknown code stops the start-up: guessing a market would scrape the
    /// wrong stores into the wrong database.
    /// </summary>
    public static SiteMarket FromConfiguration(IConfiguration configuration) =>
        configuration["Market:Code"]?.Trim().ToUpperInvariant() switch
        {
            null or "" or "US" => Us,
            "UK" or "GB" => Uk,
            var other => throw new InvalidOperationException(
                $"Unknown Market:Code '{other}'. Expected US or UK."),
        };
}
