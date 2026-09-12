namespace IndirimTakip.Infrastructure.Security;

public static class SecurityEventKinds
{
    public const string Unauthorized = "unauthorized";
    public const string RateLimited = "rate-limited";
    public const string Probe = "probe";
    public const string ServerError = "server-error";
}

/// <summary>
/// Decides whether a request is worth recording.
/// </summary>
/// <remarks>
/// Kept as a pure function because the real risk is HERE: a rule too broad records
/// normal traffic too (volume + needless personal data), a rule too narrow misses
/// a real attack. Being pure, it can be tested.
/// </remarks>
public static class SecurityEventClassifier
{
    // Known exploit scan markers. All ASCII and they must stay that way; the
    // comparison below is ASCII-based too.
    private static readonly string[] ProbeMarkers =
    [
        ".php", ".env", ".git", ".bak", ".sql", ".yml", ".ini",
        "wp-admin", "wp-login", "wp-content", "wp-includes", "xmlrpc",
        "phpmyadmin", "/vendor/", "/cgi-bin/", "/shell", "/.aws", "/.ssh",
        "eval-stdin", "config.json", "credentials", "/actuator", "/solr",
    ];

    /// <summary>
    /// The event kind to record; <c>null</c> if the request isn't noteworthy.
    /// </summary>
    public static string? Classify(int statusCode, string path)
    {
        if (statusCode == 429)
            return SecurityEventKinds.RateLimited;

        if (statusCode is 401 or 403)
            return SecurityEventKinds.Unauthorized;

        if (statusCode >= 500)
            return SecurityEventKinds.ServerError;

        // MOST 404s are innocent (deleted product, old link, typo), and recording
        // them all would fill the table with noise. Only those carrying known
        // attack patterns are taken.
        if (statusCode == 404 && LooksLikeProbe(path))
            return SecurityEventKinds.Probe;

        return null;
    }

    public static bool LooksLikeProbe(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // INVARIANT IS RIGHT HERE: every marker is ASCII. Lowercasing with a
        // Turkish culture would turn "I" into a dotless "ı" and a path such as
        // ".INI" would stop matching. There is no trap the other way: a
        // non-ASCII letter in the path stays as is and shouldn't match any
        // ASCII marker anyway.
        var lower = path.ToLowerInvariant();

        foreach (var marker in ProbeMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
