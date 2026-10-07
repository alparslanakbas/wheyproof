namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// Affiliate link settings. Values come from configuration (the server's .env)
/// and never enter the repo: they are account-bound.
/// </summary>
public sealed class AffiliateOptions
{
    /// <summary>
    /// One rule per store. A LIST, NOT A DICTIONARY KEYED BY HOST: the host
    /// would end up in the environment variable name
    /// (Affiliate__Links__transparentlabs.com), and in the production Linux
    /// container those dotted names never reached the configuration. The
    /// variables were in the container, the app loaded 0 rules and every link
    /// stayed untracked, while the same binding passed on Windows. Here the
    /// host is a value: Affiliate__Rules__0__Host / Affiliate__Rules__0__Link.
    /// </summary>
    public List<AffiliateRule> Rules { get; set; } = [];

    /// <summary>
    /// Redirect template (<c>{url}</c>) for stores WITHOUT a rule: Sovrn Commerce
    /// (<c>https://redirect.viglink.com?key=...&amp;u={url}</c>), which pays for the stores in its network and
    /// sends the rest straight through. A store's own rule always wins. Empty = off, links stay as they are.
    /// Why (2026-10-07, approved that day): about 80% of US products and most clicks went to stores with no
    /// programme of ours (Nutricost, Bodybuilding.com, BulkSupplements...); one key covers them all, and stores
    /// added later are covered without a new rule.
    /// </summary>
    public string? Fallback { get; set; }
}

public sealed class AffiliateRule
{
    /// <summary>Store host, with or without "www.".</summary>
    public string? Host { get; set; }

    /// <summary>
    /// Two shapes, matching how the networks build links:
    /// <list type="bullet">
    /// <item>A query pair, appended to the product URL. UpPromote and Refersion
    /// work this way: <c>sca_ref=12345.abcde</c>, <c>rfsn=123456.abc123</c>.</item>
    /// <item>A redirect template containing <c>{url}</c>, replaced by the encoded
    /// product URL. Awin, CJ, Impact and Sovrn send the click through their own
    /// domain: <c>https://www.awin1.com/cread.php?awinmid=1&amp;awinaffid=2&amp;ued={url}</c>.</item>
    /// </list>
    /// </summary>
    public string? Link { get; set; }
}

/// <summary>
/// Turns a product URL into the affiliate link for the store that sells it.
/// </summary>
public static class AffiliateLinkBuilder
{
    private const string UrlPlaceholder = "{url}";

    /// <summary>
    /// KEYED BY THE STORE'S HOST, NOT THE BRAND. Bodybuilding.com sells Optimum
    /// Nutrition, Ghost and others under their own brand names; a brand-keyed
    /// code would put Optimum's tracking on a bodybuilding.com URL, where nobody
    /// reads it, and the sale would earn nothing. The host is the store that
    /// pays the commission.
    ///
    /// Without a rule the network fallback applies (see <see cref="AffiliateOptions.Fallback"/>); with neither, or
    /// with a rule that doesn't have a recognizable shape, the URL comes back unchanged: the shopper reaching the
    /// store comes before the click being tracked.
    /// </summary>
    public static string Apply(string url, AffiliateOptions options)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = NormalizeHost(uri.Host);
        var rule = (options.Rules ?? [])
            .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Host) && !string.IsNullOrWhiteSpace(r.Link)
                                 && NormalizeHost(r.Host) == host)
            ?.Link?.Trim();
        if (string.IsNullOrEmpty(rule))
        {
            // Only a redirect template makes sense for a store we know nothing about; a query pair is store-specific.
            var fallback = options.Fallback?.Trim();
            return !string.IsNullOrEmpty(fallback) && fallback.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase)
                ? Redirect(fallback, url)
                : url;
        }

        return rule.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase)
            ? Redirect(rule, url)
            : AppendQueryPair(url, rule);
    }

    /// <summary>Whether stores without a rule go through the network fallback, for the startup log.</summary>
    public static bool HasFallback(AffiliateOptions options) =>
        options.Fallback?.Trim() is { Length: > 0 } f
        && f.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase)
        && f.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // A redirect template must itself be an https address; anything else is a configuration mistake, not a link to
    // send people to.
    private static string Redirect(string template, string url) =>
        template.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? template.Replace(UrlPlaceholder, Uri.EscapeDataString(url), StringComparison.OrdinalIgnoreCase)
            : url;

    /// <summary>Hosts that have a usable rule, for the startup log.</summary>
    public static IEnumerable<string> ConfiguredHosts(AffiliateOptions options) =>
        (options.Rules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Host) && !string.IsNullOrWhiteSpace(r.Link))
            .Select(r => NormalizeHost(r.Host!))
            .Order();

    private static string AppendQueryPair(string url, string pair)
    {
        var separatorIndex = pair.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == pair.Length - 1)
            return url;

        var parameter = pair[..separatorIndex];

        // If the URL already has the same parameter it isn't added twice; which
        // one the store reads would be ambiguous.
        if (url.Contains($"?{parameter}=", StringComparison.OrdinalIgnoreCase) ||
            url.Contains($"&{parameter}=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

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

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.').ToLowerInvariant();
        return trimmed.StartsWith("www.", StringComparison.Ordinal) ? trimmed[4..] : trimmed;
    }
}
