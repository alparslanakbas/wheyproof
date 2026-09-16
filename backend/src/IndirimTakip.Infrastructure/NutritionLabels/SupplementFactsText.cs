using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>One Supplement Facts row as read: name, amount per serving, unit.</summary>
internal sealed record SupplementFactsRow(string Label, decimal Amount, string Unit)
{
    public string AmountText => Amount.ToString("0.###", CultureInfo.InvariantCulture)
        + (Unit is "g" or "mg" or "mcg" ? Unit : " " + Unit);
}

/// <summary>A panel read from one OCR pass. <see cref="Problem"/> is null only when every row was read.</summary>
internal sealed record SupplementFactsPanel(IReadOnlyList<SupplementFactsRow> Rows, string? Problem);

/// <summary>
/// Reads the named rows of a Supplement Facts panel from OCR text (creatine,
/// amino acids, pre-workout, vitamins), which print no calories to check.
/// </summary>
/// <remarks>
/// Measured before writing this, on 26 labels from 17 brands read by eye
/// (158 rows, 2026-09-17): the best OCR pass read 69% of rows right and only
/// 1 amount wrong, but names came out garbled ("lron") and rows whose name
/// wraps onto the next line produced false rows. So nothing is published
/// unless ALL of these hold:
///
/// <b>Complete.</b> A panel line that shows an amount the parser can't take
/// (a unit read as a digit: "5g" as "59"; a name continued from the line
/// above) rejects the label. A table missing the caffeine row of a
/// pre-workout is worse than the page's "no table" note.
///
/// <b>Known names.</b> Every row name must match
/// <see cref="SupplementIngredientNames"/>.
///
/// <b>Two OCR passes agree</b> on every row, name, amount and unit
/// (<see cref="Agree"/>). A misread rarely repeats identically in another
/// page segmentation mode.
///
/// <b>Grams fit the serving.</b> "7 g" read as "79g" in a 20 g scoop is refused.
/// </remarks>
internal static partial class SupplementFactsText
{
    /// <summary>Reads one OCR pass.</summary>
    public static SupplementFactsPanel Read(string text)
    {
        var lines = LabelTextParser.Normalize(text).Split('\n');

        // Some passes lose the big header line; the serving size line opens the
        // same panel. Not on a Nutrition Facts panel: that one is the calorie
        // check's, and its free rows (sodium, vitamins) must not come through here.
        var start = Array.FindIndex(lines, l => HeaderRegex().IsMatch(l));
        if (start < 0 && lines.Any(l => NutritionHeaderRegex().IsMatch(l)))
            return Fail("a Nutrition Facts panel");
        if (start < 0)
            start = Array.FindIndex(lines, l => ServingSizeRegex().IsMatch(l));
        if (start < 0)
            return Fail("no Supplement Facts header");

        var rows = new List<SupplementFactsRow>();

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = LeadingJunkRegex().Replace(lines[i].Trim(), "");
            // Lines with no word in them are OCR noise or a lone big number ("140" under "Calories").
            if (line.Count(char.IsLetter) < 3)
                continue;
            if (EndRegex().IsMatch(line))
                break;
            if (PanelHeaderRegex().IsMatch(line) || MacroRegex().IsMatch(line))
                continue;

            var amount = AmountRegex().Match(line);
            if (!amount.Success)
            {
                // "CarnoSyn Beta-Alanine 20" ("2g"): a number with no unit is a row we can't read.
                if (UnreadAmountRegex().IsMatch(StripNoise(line)))
                    return Fail($"a row's amount has no readable unit: '{Shorten(line)}'");
                continue;
            }

            var rawName = line[..amount.Index].Trim();

            // "5g CREATINE": a callout printed beside the panel, read into its lines.
            // A bare amount with nothing after it is a row that lost its name.
            if (rawName.Length == 0)
            {
                if (line[(amount.Index + amount.Length)..].Count(char.IsLetter) >= 3)
                    continue;
                return Fail($"an amount without a name: '{Shorten(line)}'");
            }

            // An amount on a line that starts inside a name or a note can't be
            // placed safely. "(Calci-K), Aquamin S sea minerals, calcium silicate)
            // 155mg" finishes a wrapped name; "(Purcaf Organic Caffeine (from 325 mg"
            // IS the caffeine row; "(from 1,000mg of magnesium glycinate)" is only a
            // note. They can't be told apart, and skipping the second would publish
            // a pre-workout table without its caffeine, so the label is refused.
            if (rawName[0] == '(' || char.IsLower(rawName[0]))
                return Fail($"an amount on a continued line: '{Shorten(line)}'");

            // "<1g" is not an amount.
            if (amount.Index > 0 && line[amount.Index - 1] == '<')
                continue;

            var value = decimal.Parse(amount.Groups["num"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture);
            if (value == 0)
                continue;

            var name = CleanName(rawName);
            if (name.Count(char.IsLetter) < 3)
                return Fail($"a row has no readable name: '{Shorten(line)}'");
            // The note can hold the known name: "CreaSol SSAT (Stabilized Tyrosol)".
            // The published name stays the short one.
            if (!SupplementIngredientNames.IsKnown(name) && !SupplementIngredientNames.IsKnown(rawName))
                return Fail($"unknown row name '{name}'");
            if (rows.Any(r => string.Equals(r.Label, name, StringComparison.OrdinalIgnoreCase)))
                return Fail($"'{name}' appears twice");

            rows.Add(new SupplementFactsRow(name, value, Unit(amount.Groups["unit"].Value)));
        }

        return rows.Count == 0 ? Fail("no rows with an amount") : new SupplementFactsPanel(rows, null);
    }

    /// <summary>
    /// The reading EVERY given pass reads completely and identically, or the reason
    /// none can be published.
    /// </summary>
    /// <remarks>
    /// <b>OCR can drop a whole row and leave no trace</b>, so one pass calling its
    /// panel complete proves little: on RAW Nutrition's Creatine + HMB, psm 3 read
    /// 4 rows as a complete panel while psm 6 read all 7. Only a second pass that
    /// reads the same rows catches it. The reader passes its two full-layout modes
    /// (psm 3 and 6); sparse-text psm 11 drops the most rows, and agreeing with it
    /// would let two passes that lost the same rows through. On the 26-label
    /// sample every accepted panel came from psm 3 and 6 agreeing.
    /// </remarks>
    public static (NutritionLabelReading? Reading, string Reason) Agree(IReadOnlyList<string> passes)
    {
        if (passes.Count < 2)
            return (null, "supplement facts: needs two OCR passes to compare");

        var panels = passes.Select(Read).ToList();
        if (panels.FirstOrDefault(p => p.Problem is not null) is { } incomplete)
            return (null, "supplement facts: " + incomplete.Problem);

        var first = panels[0].Rows;
        if (panels.Skip(1).Any(p => !SameRows(first, p.Rows)))
            return (null, "supplement facts: OCR passes read different rows");

        var serving = passes.Select(t => LabelTextParser.Parse(t).ServingSizeGrams).FirstOrDefault(s => s is > 0);
        var tooHeavy = first.FirstOrDefault(r => r.Unit == "g" && serving is > 0 && r.Amount > serving * 1.05m + 1);
        if (tooHeavy is not null)
            return (null, $"supplement facts: '{tooHeavy.Label}' {tooHeavy.Amount} g is more than the {serving} g serving");

        var rows = new List<NutritionLabelRow>();
        if (serving is { } grams)
            rows.Add(new NutritionLabelRow("Serving Size", grams.ToString("0.##", CultureInfo.InvariantCulture) + "g"));
        rows.AddRange(first.Select(r => new NutritionLabelRow(r.Label, r.AmountText)));

        return (new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = "Supplement Facts",
            ServingSizeGrams = serving,
            Rows = rows,
            RowsCrossChecked = true,
        }, "supplement facts rows agreed across OCR passes");
    }

    private static bool SameRows(IReadOnlyList<SupplementFactsRow> a, IReadOnlyList<SupplementFactsRow> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            p.First.Amount == p.Second.Amount && p.First.Unit == p.Second.Unit
            && SupplementIngredientNames.Fold(p.First.Label) == SupplementIngredientNames.Fold(p.Second.Label));

    // "Vitamin B6 (as Pyridoxal-5-Phosphate)" -> "Vitamin B6"; marks and trailing punctuation go.
    // Dashes become a hyphen: OCR prints "L–Tyrosine" and "L—Tyrosine" for one label.
    private static string CleanName(string raw)
    {
        var withoutNotes = ParentheticalRegex().Replace(raw.Replace('–', '-').Replace('—', '-'), " ");
        var withoutMarks = MarkRegex().Replace(withoutNotes, " ");
        return SpaceRegex().Replace(withoutMarks, " ").Trim(' ', '.', ':', ',', '-', '—', '|', '*', '†');
    }

    // Percentages, ratios ("2:1:1") and parenthetical notes carry digits that aren't amounts.
    private static string StripNoise(string line) =>
        RatioRegex().Replace(PercentRegex().Replace(ParentheticalRegex().Replace(line, " "), " "), " ");

    private static string Unit(string raw)
    {
        var unit = raw.ToLowerInvariant().Replace(" ", "");
        return unit switch
        {
            "µg" or "mcg" => "mcg",
            "mg" => "mg",
            "g" => "g",
            "iu" => "IU",
            _ => "billion CFU",
        };
    }

    private static string Shorten(string line) => line.Length <= 50 ? line : line[..50] + "…";

    private static SupplementFactsPanel Fail(string problem) => new([], problem);

    [GeneratedRegex(@"supplement\s*facts", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();

    // The panel ends at its footnotes or the ingredient list.
    [GeneratedRegex(@"not\s+established|based\s+on\s+a\s*2|other\s+ingredients|^ingredients", RegexOptions.IgnoreCase)]
    private static partial Regex EndRegex();

    [GeneratedRegex(@"serving\s*size", RegexOptions.IgnoreCase)]
    private static partial Regex ServingSizeRegex();

    [GeneratedRegex(@"nutrition\s*facts", RegexOptions.IgnoreCase)]
    private static partial Regex NutritionHeaderRegex();

    // Anywhere in the line: OCR puts noise in front ("B! Serving Size 1 Scoop") and
    // text beside the panel into it ("Per Container: 30 To maximize results").
    [GeneratedRegex(@"serving\s*size|servings?\s+per|per\s+container|amount\s*per|amt\.?\s*per|%\s*d(?:aily|v)|daily\s+value|^\d+\s+servings|^about\s+\d+", RegexOptions.IgnoreCase)]
    private static partial Regex PanelHeaderRegex();

    // Rows that belong to the macro fields; not published as named rows, not a gap either.
    // Up to two 1-2 letter noise tokens may come first ("SW) Calories 5", "ae | Calories 50");
    // a real word may not, so "Whey Protein 20g" is never taken for the protein field.
    [GeneratedRegex(@"^[^A-Za-z]*(?:[A-Za-z]{1,2}[^A-Za-z]+){0,2}(?:calories|total\s+fat|saturated|trans\s|cholesterol|total\s+carb|dietary\s+fiber|protein\b|includes)", RegexOptions.IgnoreCase)]
    private static partial Regex MacroRegex();

    // The first number with a unit. "(?<![\w.,])" keeps "B12" and "5-Phosphate" out of it.
    // "[tT]{0,2}": the "†" footnote mark after a unit reads as "t" ("5gt", "200mg Tt").
    [GeneratedRegex(@"(?<![\w.,])(?<num>\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)\s*(?<unit>mcg|µg|mg|g|iu|billion\s*cfus?)[tT]{0,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"\d")]
    private static partial Regex UnreadAmountRegex();

    [GeneratedRegex(@"^[^A-Za-z0-9(]+")]
    private static partial Regex LeadingJunkRegex();

    [GeneratedRegex(@"\([^)]*\)?")]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s*%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"\d+(?::\d+)+")]
    private static partial Regex RatioRegex();

    [GeneratedRegex(@"[®™©@*†‡]")]
    private static partial Regex MarkRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
