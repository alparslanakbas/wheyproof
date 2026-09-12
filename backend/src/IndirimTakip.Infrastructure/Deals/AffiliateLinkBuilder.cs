namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// Brands' affiliate program settings. Codes come from configuration (the
/// server's .env) and never enter the repo: they are account-bound.
/// Example: Affiliate__TrackingCodes__SomeBrand=765c5a13e1
/// </summary>
public sealed class AffiliateOptions
{
    /// <summary>Brand name -> tracking code. Brand names match case-insensitively.</summary>
    public Dictionary<string, string> TrackingCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Name of the tracking parameter. OpenCart's affiliate module uses
    /// "tracking"; it can be split per brand if a store on another platform is
    /// added.
    /// </summary>
    public string ParameterName { get; set; } = "tracking";
}

/// <summary>
/// Adds the brand's affiliate tracking code to a product URL.
/// </summary>
public static class AffiliateLinkBuilder
{
    /// <summary>
    /// Adds the tracking parameter when a code is configured; otherwise returns
    /// the URL unchanged. A malformed URL is left alone too: the redirect working
    /// comes before it being tracked.
    /// </summary>
    public static string Apply(string url, string? brandName, AffiliateOptions options)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(brandName))
            return url;

        if (options.TrackingCodes is null ||
            !options.TrackingCodes.TryGetValue(brandName, out var code) ||
            string.IsNullOrWhiteSpace(code))
        {
            return url;
        }

        var parameter = string.IsNullOrWhiteSpace(options.ParameterName) ? "tracking" : options.ParameterName;

        // If the URL already has the same parameter it isn't added twice; which
        // one the store reads would be ambiguous.
        if (url.Contains($"?{parameter}=", StringComparison.OrdinalIgnoreCase) ||
            url.Contains($"&{parameter}=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var pair = $"{Uri.EscapeDataString(parameter)}={Uri.EscapeDataString(code)}";

        // With an existing query string it must be appended with &, otherwise
        // with ?; the wrong separator breaks the link entirely. If there is a
        // fragment (#), the parameter must come BEFORE it, or the server never
        // sees it.
        var fragmentIndex = url.IndexOf('#');
        var basePart = fragmentIndex >= 0 ? url[..fragmentIndex] : url;
        var fragment = fragmentIndex >= 0 ? url[fragmentIndex..] : string.Empty;

        var separator = basePart.Contains('?') ? '&' : '?';
        return $"{basePart}{separator}{pair}{fragment}";
    }
}
