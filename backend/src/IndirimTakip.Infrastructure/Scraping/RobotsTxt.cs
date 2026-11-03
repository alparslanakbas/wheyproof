using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// The rules of one robots.txt that apply to our bot (RFC 9309).
/// </summary>
/// <remarks>
/// <b>WHY (2026-10-08).</b> Shopify's bot registration asks us to confirm that the bot follows each
/// store's robots.txt, and wheyproofbot is how we name ourselves there. Measured that day before
/// writing this: all 59 Shopify hosts of the three sites allow our three paths (catalog, home page,
/// product pages) and none sets a crawl-delay, so nothing changes today; this keeps it true later.
///
/// <b>Rules</b> (RFC 9309, the same as Google's): our group is the one naming our product token
/// (case-insensitive); without one, the <c>*</c> group; several matching groups merge. The longest
/// matching pattern wins and an allow wins a tie. <c>*</c> matches any sequence and a final <c>$</c>
/// anchors the end. Patterns are matched against the path with its query, as the RFC does.
///
/// <b>Crawl-delay (2026-11-03).</b> Not in the RFC, but Shopify's bot review asked how we handle
/// it, and until then it was only read past. Now the seconds in our group (or the <c>*</c> group)
/// are the least time between two requests to the host (see <see cref="RobotsTxtCache.PaceAsync"/>);
/// several matching groups take the longest. A value that isn't a positive number is ignored.
/// </remarks>
public sealed class RobotsRules
{
    private readonly List<(bool Allow, string Pattern, Regex Regex)> rules;

    private RobotsRules(List<(bool, string, Regex)> rules, TimeSpan? crawlDelay)
    {
        this.rules = rules;
        CrawlDelay = crawlDelay;
    }

    /// <summary>No rules: everything allowed (also what a missing robots.txt means, RFC 9309 2.3.1.3).</summary>
    public static readonly RobotsRules AllowAll = new([], null);

    /// <summary>The least time between two requests to the host, when robots.txt sets one for us.</summary>
    public TimeSpan? CrawlDelay { get; }

    private sealed class Group
    {
        public List<string> Agents { get; } = [];
        public List<(bool Allow, string Pattern)> Rules { get; } = [];
        public double? CrawlDelaySeconds { get; set; }
    }

    public static RobotsRules Parse(string text, string productToken)
    {
        var groups = new List<Group>();
        Group? current = null;
        var lastWasAgent = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var name = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (name == "user-agent")
            {
                if (!lastWasAgent || current is null)
                {
                    current = new Group();
                    groups.Add(current);
                }
                current.Agents.Add(value);
                lastWasAgent = true;
            }
            else if (name is "allow" or "disallow")
            {
                lastWasAgent = false;
                // Rules before any user-agent line belong to no group; an empty disallow allows all.
                if (current is not null && value.Length > 0)
                    current.Rules.Add((name == "allow", value));
            }
            else if (name == "crawl-delay")
            {
                lastWasAgent = false;
                if (current is not null
                    && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds > 0 && double.IsFinite(seconds))
                    current.CrawlDelaySeconds = Math.Max(current.CrawlDelaySeconds ?? 0, seconds);
            }
            else
            {
                lastWasAgent = false;
            }
        }

        var ours = groups.Where(g => g.Agents.Any(a => a.Equals(productToken, StringComparison.OrdinalIgnoreCase))).ToList();
        if (ours.Count == 0)
            ours = groups.Where(g => g.Agents.Contains("*")).ToList();

        var delay = ours.Max(g => g.CrawlDelaySeconds);
        return new RobotsRules(ours.SelectMany(g => g.Rules)
            .Select(r => (r.Allow, r.Pattern, ToRegex(r.Pattern)))
            // Capped at a day only to keep TimeSpan in range; anything that long refuses the host anyway.
            .ToList(), delay is { } s ? TimeSpan.FromSeconds(Math.Min(s, TimeSpan.FromDays(1).TotalSeconds)) : null);
    }

    /// <summary>Whether <paramref name="pathAndQuery"/> may be fetched.</summary>
    public bool Allows(string pathAndQuery)
    {
        if (pathAndQuery.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            return true;

        var best = rules
            .Where(r => r.Regex.IsMatch(pathAndQuery))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenByDescending(r => r.Allow)
            .FirstOrDefault();
        return best.Pattern is null || best.Allow;
    }

    private static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = Regex.Escape(anchored ? pattern[..^1] : pattern).Replace(@"\*", ".*");
        return new Regex("^" + body + (anchored ? "$" : ""), RegexOptions.CultureInvariant);
    }
}
