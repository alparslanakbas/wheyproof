using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Pulls the calorie-checkable values out of OCR text of a US facts panel.
/// </summary>
/// <remarks>
/// <b>Only fields the calorie check covers are returned as rows.</b> Serving
/// size, calories, fat, carbohydrate, fiber and protein either feed the 4/4/9
/// sum or bound it. Sodium, sugars or vitamins would be published unverified,
/// and a misread there ("250mg" as "2500mg") has nothing to catch it.
///
/// <b>Common OCR confusions are fixed before matching</b>, measured on real
/// labels: "0g" comes out as "Og" and "1g" as "lg". The fix applies only
/// right before a gram or milligram unit, and anything it gets wrong still has
/// to pass the calorie check.
/// </remarks>
internal static partial class LabelTextParser
{
    private const string Num = @"(\d{1,4}(?:\.\d{1,2})?)";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ServingRegex = new(@"serving\s*size[^\n]*?" + Num + @"\s*g\b", Options);
    // A number must follow directly: "2,000 calories a day" in the footnote
    // has its number before the word, so it never matches. The lookahead also
    // refuses a shortened number: without "[\d.]" in it, "Calories 10%" would
    // backtrack to "1" and pass. It looks at THE SAME LINE only ([ \t], not \s):
    // a real panel prints "% Daily Value" on the line under "Calories 160", and
    // a lookahead crossing the line break rejected every such label (caught by
    // the Orgain test).
    //
    // "\.\d", not ".": page text writes "Calories: 180." with a sentence period,
    // which is not a decimal and must not reject the number.
    //
    // "[:\s]*" after each label: product pages write "Total Fat: 7g", OCR'd
    // labels "Total Fat 7g".
    private static readonly Regex CaloriesRegex = new(@"calories[:\s]*" + Num + @"(?!\d|\.\d|[ \t]*(?:m?g|%))", Options);
    private static readonly Regex FatRegex = new(@"total\s*fat[:\s]*" + Num + @"\s*g\b", Options);
    private static readonly Regex CarbsRegex = new(@"total\s*carb\w*\.?[:\s]*" + Num + @"\s*g\b", Options);
    private static readonly Regex FiberRegex = new(@"fiber[:\s]*" + Num + @"\s*g\b", Options);
    private static readonly Regex SugarAlcoholRegex = new(@"(?:sugar\s*alcohols?|erythritol|allulose)[:\s]*" + Num + @"\s*g\b", Options);
    private static readonly Regex ProteinRegex = new(@"protein[:\s]*" + Num + @"\s*g\b", Options);

    public static NutritionLabelReading Parse(string text)
    {
        var normalized = Normalize(text);

        var panel = NutritionFactsRegex().IsMatch(normalized) ? "Nutrition Facts"
            : SupplementFactsRegex().IsMatch(normalized) ? "Supplement Facts"
            : null;

        if (panel is null)
            return new NutritionLabelReading { IsNutritionLabel = false };

        var reading = new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = panel,
            ServingSizeGrams = Grab(ServingRegex, normalized),
            Calories = Grab(CaloriesRegex, normalized),
            FatGrams = Grab(FatRegex, normalized),
            CarbohydrateGrams = Grab(CarbsRegex, normalized),
            FiberGrams = Grab(FiberRegex, normalized),
            SugarAlcoholGrams = Grab(SugarAlcoholRegex, normalized),
            ProteinGrams = Grab(ProteinRegex, normalized),
        };

        return reading with { Rows = CheckedRows(reading) };
    }

    /// <summary>
    /// The rows published for a reading: only the fields the calorie check covers,
    /// in one order and wording whichever source (OCR text or page JSON) read them.
    /// </summary>
    internal static List<NutritionLabelRow> CheckedRows(NutritionLabelReading reading)
    {
        var rows = new List<NutritionLabelRow>();
        AddRow(rows, "Serving Size", reading.ServingSizeGrams, "g");
        AddRow(rows, "Calories", reading.Calories, "");
        AddRow(rows, "Total Fat", reading.FatGrams, "g");
        AddRow(rows, "Total Carbohydrate", reading.CarbohydrateGrams, "g");
        AddRow(rows, "Dietary Fiber", reading.FiberGrams, "g");
        AddRow(rows, "Protein", reading.ProteinGrams, "g");
        return rows;
    }

    internal static string Normalize(string text)
    {
        var collapsed = HorizontalSpaceRegex().Replace(text, " ");
        collapsed = ZeroBeforeUnitRegex().Replace(collapsed, "0");
        return OneBeforeUnitRegex().Replace(collapsed, "1");
    }

    private static decimal? Grab(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static void AddRow(List<NutritionLabelRow> rows, string label, decimal? value, string unit)
    {
        if (value is { } v)
            rows.Add(new NutritionLabelRow(label, v.ToString("0.##", CultureInfo.InvariantCulture) + unit));
    }

    [GeneratedRegex(@"nutrition\s*facts", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NutritionFactsRegex();

    [GeneratedRegex(@"supplement\s*facts", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SupplementFactsRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex HorizontalSpaceRegex();

    // "Trans Fat Og" -> "0g", "O.5g" -> "0.5g"; only right after a space or colon.
    [GeneratedRegex(@"(?<=[\s:])[Oo](?=(?:\.\d)?\s*m?g\b)")]
    private static partial Regex ZeroBeforeUnitRegex();

    // "Saturated Fat lg" -> "1g".
    [GeneratedRegex(@"(?<=[\s:])[lI|](?=(?:\.\d)?\s*m?g\b)")]
    private static partial Regex OneBeforeUnitRegex();
}
