using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

// Nutrition tables come in different HTML structures per store (div rows,
// <table>, a table inside the description, HTML inside embedded JSON). Each
// scraper extracts raw (label, value) pairs from its own structure; normalizing,
// converting to JSON and parsing the protein value happen here, in one place.
internal static class NutritionParser
{
    // The longest label accepted for a nutrition table row. Longer "labels" are
    // usually a description paragraph mixed in, not a table row; they're skipped.
    private const int MaxLabelLength = 60;
    private const int MaxValueLength = 40;

    // Some stores use bilingual labels such as "Protein / Protein"; the first part
    // is enough, the second repeats it.
    private static readonly Regex DuplicateLabelPattern = new(@"^(.+?)\s*/\s*(.+)$", RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    // Pulls the number from values like "24 g", "24,5g", "1.049 mg", "120 kcal".
    private static readonly Regex NumberPattern = new(@"(\d+(?:[.,]\d+)?)", RegexOptions.Compiled);

    // The protein row's label varies by store and language: "Protein",
    // "Protein / Protein", "Protein (g)", "Total Protein"... All contain "protein",
    // but extra words are excluded to keep out product name rows such as "Protein
    // Powder" ("tozu" and "kaynak" are the Turkish words for powder and source).
    private static readonly string[] ProteinExclusions = ["tozu", "kaynak", "source", "blend", "matrix"];

    // Builds a normalized nutrition table from raw (label, value) pairs. Returns null
    // when there is no meaningful row: "no data" is preferred over storing an empty
    // table.
    public static string? BuildNutritionJson(IEnumerable<(string Label, string Value)> rows)
    {
        var table = new Dictionary<string, string>();

        foreach (var (rawLabel, rawValue) in rows)
        {
            var label = NormalizeLabel(rawLabel);
            var value = Normalize(rawValue);

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value))
                continue;
            if (label.Length > MaxLabelLength || value.Length > MaxValueLength)
                continue;
            // The value must contain at least one number; this drops text rows
            // mixed into the table, such as "Product description: ...".
            if (!NumberPattern.IsMatch(value))
                continue;

            // If the same label appears twice the first is kept (in tables the
            // second column is usually a secondary value such as "%DV").
            table.TryAdd(label, value);
        }

        return table.Count > 0 ? JsonSerializer.Serialize(table) : null;
    }

    // Pulls protein per serving (grams) from the normalized table.
    // Null if not found; nothing is guessed.
    public static decimal? ExtractProteinGrams(string? nutritionJson)
    {
        if (string.IsNullOrEmpty(nutritionJson))
            return null;

        Dictionary<string, string>? table;
        try
        {
            table = JsonSerializer.Deserialize<Dictionary<string, string>>(nutritionJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (table is null)
            return null;

        foreach (var (label, value) in table)
        {
            var lowered = label.Replace('İ', 'i').ToLowerInvariant();
            if (!lowered.Contains("protein"))
                continue;
            if (ProteinExclusions.Any(lowered.Contains))
                continue;

            // The value must be in grams; "mg" or "kcal" means this row isn't the
            // protein amount (e.g. "Calories from protein").
            var loweredValue = value.ToLowerInvariant();
            if (loweredValue.Contains("kcal") || loweredValue.Contains("kj") || loweredValue.Contains("mg"))
                continue;

            var match = NumberPattern.Match(value);
            if (!match.Success)
                continue;

            if (decimal.TryParse(
                    match.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var grams)
                // A reasonable range for protein per serving; matches outside it
                // point to the wrong row being captured.
                && grams > 0 && grams <= 100)
            {
                return grams;
            }
        }

        return null;
    }

    private static string NormalizeLabel(string rawLabel)
    {
        var label = Normalize(rawLabel).TrimEnd(':', '.');

        // "Protein / Protein" → "Protein" (when it's the same word repeated).
        var duplicate = DuplicateLabelPattern.Match(label);
        if (duplicate.Success)
        {
            var first = duplicate.Groups[1].Value.Trim();
            var second = duplicate.Groups[2].Value.Trim();
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                return first;
        }

        return label;
    }

    private static string Normalize(string raw) =>
        WhitespacePattern.Replace(raw.Replace("&nbsp;", " ").Replace(' ', ' '), " ").Trim();
}
