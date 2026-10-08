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
/// </remarks>
public sealed class RobotsRules
{
    private readonly List<(bool Allow, string Pattern, Regex Regex)> rules;

    private RobotsRules(List<(bool, string, Regex)> rules) => this.rules = rules;

    /// <summary>No rules: everything allowed (also what a missing robots.txt means, RFC 9309 2.3.1.3).</summary>
    public static readonly RobotsRules AllowAll = new([]);

    public static RobotsRules Parse(string text, string productToken)
    {
        var groups = new List<(List<string> Agents, List<(bool Allow, string Pattern)> Rules)>();
        List<string>? agents = null;
        List<(bool, string)>? current = null;
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
                if (!lastWasAgent || agents is null)
                {
                    agents = [];
                    current = [];
                    groups.Add((agents, current));
                }
                agents.Add(value);
                lastWasAgent = true;
            }
            else if (name is "allow" or "disallow")
            {
                lastWasAgent = false;
                // Rules before any user-agent line belong to no group; an empty disallow allows all.
                if (current is not null && value.Length > 0)
                    current.Add((name == "allow", value));
            }
            else
            {
                lastWasAgent = false;
            }
        }

        var ours = groups.Where(g => g.Agents.Any(a => a.Equals(productToken, StringComparison.OrdinalIgnoreCase))).ToList();
        if (ours.Count == 0)
            ours = groups.Where(g => g.Agents.Contains("*")).ToList();

        return new RobotsRules(ours.SelectMany(g => g.Rules)
            .Select(r => (r.Allow, r.Pattern, ToRegex(r.Pattern)))
            .ToList());
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
