using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Pulls the number from rows such as "Serving Size: 32g" / "Servings: 30".
/// </summary>
/// <remarks>
/// <b>Why shared.</b> Several stores give the same information in nearly the same
/// shape ("32g" vs "30 Gram", "68" vs "30 Servings"). Copying the parsing into
/// every scraper would let the reasonable-range checks below drift apart over time;
/// brand aliases in this codebase broke in exactly that way.
///
/// The range checks are DELIBERATE: a match outside the range points to the wrong
/// row being captured, and a made-up serving size would silently inflate the price
/// per serving many times over. Null instead of a suspicious value.
/// </remarks>
internal static class NutritionServingParser
{
    // Grams: "32g", "30 Gram", "1,5 gr": a unit starting with "g" must follow the
    // number. The unit requirement matters: without it, an "Expiry date: 01/04/2029"
    // line in the same div would yield 1.
    private static readonly Regex GramPattern =
        new(@"(\d+(?:[.,]\d+)?)\s*g", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CountPattern = new(@"(\d+)", RegexOptions.Compiled);

    /// <summary>Serving size (grams). Null if outside a reasonable range.</summary>
    public static decimal? Grams(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = GramPattern.Match(text);
        if (!match.Success)
            return null;

        return decimal.TryParse(
                match.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var grams)
            && grams > 0 && grams <= 500
            ? grams
            : null;
    }

    /// <summary>Servings per package. Null if outside a reasonable range.</summary>
    public static int? Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = CountPattern.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) && count is > 0 and <= 1000
            ? count
            : null;
    }
}
