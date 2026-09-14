using System.Net;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Finds a Nutrition Facts panel in a product page's HTML.
/// </summary>
/// <remarks>
/// <b>Every "Nutrition Facts" on the page is tried, not the first.</b> Measured
/// on Quest (2026-09-14): a page carries a text summary ("Calories: 180. Total
/// Fat: 7g"), a table, and a description line "See Nutrition Facts for Calories
/// Content". Starting from that line would read no calories, or pick up stray
/// numbers after it. A window is accepted only when it passes the calorie check.
///
/// <b>One product per page.</b> On Quest every flavor is its own product page,
/// so the panels on a page all describe the same product; the Turkish site's
/// trap of a page carrying other products' panels doesn't apply here.
///
/// <b>Tags become line breaks, not spaces.</b> Cells keep their own lines, so
/// "Calories 180" and the "% Daily Value" cell under it stay apart.
/// </remarks>
internal static partial class PageNutritionText
{
    // A panel from its heading through protein fits well within this.
    private const int WindowLength = 1500;

    public static NutritionLabelReading? Read(string html)
    {
        var withoutCode = CodeBlockRegex().Replace(html, "\n");
        var text = WebUtility.HtmlDecode(TagRegex().Replace(withoutCode, "\n"));

        foreach (Match heading in HeadingRegex().Matches(text))
        {
            var window = text.Substring(heading.Index, Math.Min(WindowLength, text.Length - heading.Index));
            var reading = LabelTextParser.Parse(window);
            if (NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted)
                return reading;
        }

        return null;
    }

    [GeneratedRegex(@"<(script|style|noscript)\b[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"nutrition\s*facts", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();
}
