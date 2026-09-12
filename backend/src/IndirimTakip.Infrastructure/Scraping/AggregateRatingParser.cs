using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Reads the star average and review count a store shows on its own site from the
/// product page's schema.org <c>aggregateRating</c> block.
///
/// Why one parser is enough: stores with rating data ALL publish it in the product
/// page's JSON-LD markup with the same standard fields; the platforms differ, the
/// output doesn't. No per-store parser is needed.
///
/// Returns null when no value is found; nothing is guessed.
/// </summary>
internal static partial class AggregateRatingParser
{
    // A rating outside 0-5 means either a different scale or the wrong field was
    // captured; not storing it is right in both cases.
    private const decimal MinRating = 0m;
    private const decimal MaxRating = 5m;

    // "5 out of 5" from a single review isn't an average. Products below this
    // threshold count as unrated: putting "5.0 from 2 reviews" next to "4.88 from
    // 278 reviews" in a ranking would be misleading.
    public const int MinimumMeaningfulRatingCount = 3;

    public static (decimal? Value, int? Count) Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return (null, null);

        foreach (Match block in AggregateRatingBlockRegex().Matches(html))
        {
            var (value, count) = ReadBlock(block.Value);
            if (value is not null && count is not null)
                return (value, count);
        }

        return (null, null);
    }

    private static (decimal? Value, int? Count) ReadBlock(string block)
    {
        var valueMatch = RatingValueRegex().Match(block);
        var countMatch = RatingCountRegex().Match(block);
        if (!valueMatch.Success || !countMatch.Success)
            return (null, null);

        // Some sites send the value as a number ("ratingValue": 4.88), others as a
        // quoted string ("ratingValue": "4.88"); the pattern puts both in the same
        // group. The separator is always a dot (JSON), hence InvariantCulture: left
        // to the machine's locale, a culture with a comma decimal separator would
        // read "4.88" as 488.
        if (!decimal.TryParse(valueMatch.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return (null, null);
        if (!int.TryParse(countMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return (null, null);

        if (value < MinRating || value > MaxRating || count <= 0)
            return (null, null);

        return (Math.Round(value, 2), count);
    }

    // Bounding the block matters: individual reviews on the same page have their own
    // "reviewRating" blocks with a ratingValue too. Only aggregateRating is read.
    [GeneratedRegex(@"""aggregateRating""\s*:\s*\{[^{}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex AggregateRatingBlockRegex();

    [GeneratedRegex(@"""ratingValue""\s*:\s*""?(?<value>\d+(?:\.\d+)?)""?", RegexOptions.IgnoreCase)]
    private static partial Regex RatingValueRegex();

    // Sites use either reviewCount or ratingCount; both mean the same thing (how
    // many people rated).
    [GeneratedRegex(@"""(?:reviewCount|ratingCount)""\s*:\s*""?(?<count>\d+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex RatingCountRegex();
}
