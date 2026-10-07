using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Reads the first UK nutrition table on a page: "Typical Values | Per 100g | Per 30g".
/// </summary>
/// <remarks>
/// <b>Why not the US reader.</b> A UK label prints every row per 100 g AND per serving, energy in kJ and
/// kcal, and carbohydrate WITHOUT fibre: fibre is its own row at 2 kcal per gram. The US check, 4/4/9 with
/// fibre inside the carbohydrate, would be checking the wrong sum.
///
/// <b>The two columns check each other.</b> Every row's serving amount must be its per-100 g amount scaled
/// to the serving (Myprotein Impact Whey Isolate, 2026-10-07: 80 g protein per 100 g, 24 g per 30 g), and
/// the per-100 g energy must follow from the macros. A row read from the wrong line breaks the first, a
/// wrong number the second, and one failing row rejects the whole table. A table without a per-100 g
/// column has nothing to check against and is not read.
///
/// <b>Only the first table.</b> Myprotein prints one table per flavour and puts the shown flavour's first;
/// a later table never stands in for a first one that fails. The caller makes sure the first table is the
/// right one (see MyproteinScraper.FetchDetailsAsync).
///
/// <b>Rounding.</b> Labels round a small serving amount to "0g" or print "&lt;0.5g" (0.9 g fat per 100 g
/// is "0g" per 30 g), so an amount is a range and the scaling check allows 10% or 0.15 g.
///
/// <b>Published as printed, per serving.</b> The rows keep the label's own amounts and the UK names
/// ("of which saturates"); the serving column, like the US panels, because the site compares servings.
/// </remarks>
internal static partial class UkNutritionTable
{
    private enum Nutrient { Fat, Saturates, Carbohydrate, Sugars, Fibre, Protein, Salt }

    private static readonly (Nutrient Nutrient, string Label)[] Published =
    [
        (Nutrient.Fat, "Fat"), (Nutrient.Saturates, "of which saturates"),
        (Nutrient.Carbohydrate, "Carbohydrate"), (Nutrient.Sugars, "of which sugars"),
        (Nutrient.Fibre, "Fibre"), (Nutrient.Protein, "Protein"), (Nutrient.Salt, "Salt"),
    ];

    private static readonly Nutrient[] Required = [Nutrient.Fat, Nutrient.Carbohydrate, Nutrient.Protein];

    /// <summary>An amount as printed: "0.9g" is 0.9 to 0.9, "&lt;0.5g" is 0 to 0.5.</summary>
    private readonly record struct Amount(decimal Low, decimal High, string Text);

    private sealed class Column
    {
        public Dictionary<Nutrient, Amount> Grams { get; } = [];
        public decimal? Kcal { get; set; }
        public decimal? Kj { get; set; }
    }

    private sealed record Header(int Per100Column, int ServingColumn, decimal ServingGrams);

    public static NutritionLabelReading? Read(string html)
    {
        var tables = OutermostTables(html);
        var after = 0;
        foreach (Match row in LeafRowRegex().Matches(html))
        {
            if (row.Index < after || HeaderOf(Cells(row.Groups[1].Value)) is not { } header)
                continue;

            var start = row.Index + row.Length;
            var end = tables.FirstOrDefault(t => t.Start <= row.Index && row.Index < t.End).End;
            if (end <= start)
                return null;

            // An amino acid or mineral table with the same columns names no nutrient and is passed over
            // (Veloforte prints both after its nutrition table); the first table that does decides.
            var (readNutrients, reading) = ReadTable(html[start..end], header);
            if (readNutrients)
                return reading;
            after = end;
        }

        return null;
    }

    private static (bool ReadNutrients, NutritionLabelReading? Reading) ReadTable(string body, Header header)
    {
        var (per100, serving) = (new Column(), new Column());
        return ReadRows(body, header, per100, serving) switch
        {
            false => (false, null),
            null => (true, null),
            true => (true, Check(per100, serving, header.ServingGrams) ? ToReading(serving, header.ServingGrams) : null),
        };
    }

    /// <summary>
    /// Fills both columns from the rows after the header. Null when a row can't be trusted (the whole table is
    /// rejected), true when a nutrient row was read, false when none was.
    /// </summary>
    private static bool? ReadRows(string body, Header header, Column per100, Column serving)
    {
        var any = false;

        foreach (Match row in LeafRowRegex().Matches(body))
        {
            var cells = Cells(row.Groups[1].Value);
            // The next flavour's table inside the same block.
            if (HeaderOf(cells) is not null)
                break;
            if (cells.Count <= Math.Max(header.Per100Column, header.ServingColumn) || cells[0].Count == 0)
                continue;

            var labels = cells[0];
            var a = cells[header.Per100Column];
            var b = cells[header.ServingColumn];
            var namesNutrient = labels.Any(IsNutrientLabel);
            any |= namesNutrient;
            // Bulk stacks two rows in one cell ("Fat" / "of which saturates"): line k of the label goes with
            // line k of each amount. When the lines don't pair up, the amounts can't be placed.
            if (labels.Count != a.Count || labels.Count != b.Count)
            {
                if (namesNutrient)
                    return null;
                continue;
            }

            for (var k = 0; k < labels.Count; k++)
            {
                if (!Add(labels[k], a[k], b[k], per100, serving))
                    return null;
            }
        }

        return any;
    }

    private static bool IsNutrientLabel(string label) =>
        label.TrimStart().StartsWith("energy", StringComparison.OrdinalIgnoreCase) || Classify(label) is not null;

    private static bool Add(string label, string value100, string valueServing, Column per100, Column serving)
    {
        var normalized = label.ToLowerInvariant();
        if (normalized.StartsWith("energy", StringComparison.Ordinal))
            return AddEnergy(normalized, value100, per100) && AddEnergy(normalized, valueServing, serving);

        if (Classify(label) is not { } nutrient)
            return true;

        // The same nutrient twice means the rows are not what they seem.
        if (per100.Grams.ContainsKey(nutrient))
            return false;

        if (ParseGrams(value100) is not { } x || ParseGrams(valueServing) is not { } y)
            // An unreadable optional row is left out; a required one leaves nothing to check.
            return !Required.Contains(nutrient);

        per100.Grams[nutrient] = x;
        serving.Grams[nutrient] = y;
        return true;
    }

    private static bool AddEnergy(string label, string value, Column column)
    {
        var text = value.ToLowerInvariant();
        decimal? kcal = KcalRegex().Match(text) is { Success: true } c ? Number(c.Groups[1].Value) : null;
        decimal? kj = KjRegex().Match(text) is { Success: true } j ? Number(j.Groups[1].Value) : null;

        if (kcal is null && kj is null)
        {
            // Bulk: "Energy kJ/kcal" over "1542/363".
            if (PairRegex().Match(text) is { Success: true } pair && KjKcalLabelRegex().IsMatch(label))
            {
                kj = Number(pair.Groups[1].Value);
                kcal = Number(pair.Groups[2].Value);
            }
            else if (PlainNumberRegex().Match(text) is { Success: true } plain)
            {
                if (label.Contains("kcal", StringComparison.Ordinal))
                    kcal = Number(plain.Groups[1].Value);
                else if (label.Contains("kj", StringComparison.Ordinal))
                    kj = Number(plain.Groups[1].Value);
            }
        }

        if (kcal is null && kj is null)
            return false;
        if ((kcal is not null && column.Kcal is not null) || (kj is not null && column.Kj is not null))
            return false;

        column.Kcal ??= kcal;
        column.Kj ??= kj;
        return true;
    }

    private static Nutrient? Classify(string label)
    {
        var l = label.ToLowerInvariant().Trim(' ', '-', '–', '*', ':', '.');
        // Rows inside fat or carbohydrate the site doesn't publish; "mono-unsaturates" contains "saturates".
        if (l.Contains("unsaturat") || l.Contains("polyol") || l.Contains("starch") || l.Contains("trans"))
            return null;
        if (l.Contains("saturate"))
            return Nutrient.Saturates;
        if (l.Contains("sugar"))
            return Nutrient.Sugars;
        if (l.Contains("fibre") || l.Contains("fiber"))
            return Nutrient.Fibre;
        if (FatLabelRegex().IsMatch(l))
            return Nutrient.Fat;
        if (l.StartsWith("carbohydrate", StringComparison.Ordinal) || l == "carbs")
            return Nutrient.Carbohydrate;
        if (l.StartsWith("protein", StringComparison.Ordinal))
            return Nutrient.Protein;
        if (l.StartsWith("salt", StringComparison.Ordinal))
            return Nutrient.Salt;
        return null;
    }

    private static bool Check(Column per100, Column serving, decimal servingGrams)
    {
        if (servingGrams is < 1 or > 500)
            return false;
        if (per100.Kcal is not { } kcal100 || serving.Kcal is not { } kcalServing)
            return false;
        if (Required.Any(n => !per100.Grams.ContainsKey(n)))
            return false;

        decimal Low(Nutrient n) => per100.Grams.TryGetValue(n, out var a) ? a.Low : 0;
        decimal High(Nutrient n) => per100.Grams.TryGetValue(n, out var a) ? a.High : 0;

        if (Low(Nutrient.Protein) + Low(Nutrient.Carbohydrate) + Low(Nutrient.Fat) + Low(Nutrient.Fibre) > 105)
            return false;

        // EU energy factors: 4 kcal per gram of protein and carbohydrate, 9 of fat, 2 of fibre.
        var expectedLow = 4 * Low(Nutrient.Protein) + 4 * Low(Nutrient.Carbohydrate) + 9 * Low(Nutrient.Fat) + 2 * Low(Nutrient.Fibre);
        var expectedHigh = 4 * High(Nutrient.Protein) + 4 * High(Nutrient.Carbohydrate) + 9 * High(Nutrient.Fat) + 2 * High(Nutrient.Fibre);
        var tolerance = Math.Max(15m, kcal100 * 0.1m);
        if (kcal100 < expectedLow - tolerance || kcal100 > expectedHigh + tolerance)
            return false;

        // 1 kcal = 4.184 kJ, where both are printed.
        if (!KjMatchesKcal(per100) || !KjMatchesKcal(serving))
            return false;

        var factor = servingGrams / 100m;
        if (!Scales(kcal100, kcalServing, factor, 3m, 0.05m))
            return false;
        if (per100.Kj is { } kj100 && serving.Kj is { } kjServing && !Scales(kj100, kjServing, factor, 10m, 0.05m))
            return false;

        foreach (var (nutrient, amount100) in per100.Grams)
        {
            var amount = serving.Grams[nutrient];
            var low = amount100.Low * factor;
            var high = amount100.High * factor;
            // Rounded down to zero on the label.
            if (amount.High == 0 && low <= 0.5m)
                continue;
            var allowance = Math.Max(0.15m, high * 0.1m);
            if (amount.High < low - allowance || amount.Low > high + allowance)
                return false;
        }

        return serving.Grams[Nutrient.Protein].Low <= servingGrams;
    }

    private static bool KjMatchesKcal(Column column) =>
        column is not { Kj: { } kj, Kcal: { } kcal } || Math.Abs(kj - kcal * 4.184m) <= Math.Max(10m, kj * 0.05m);

    private static bool Scales(decimal per100, decimal serving, decimal factor, decimal minimum, decimal share)
    {
        var expected = per100 * factor;
        return Math.Abs(serving - expected) <= Math.Max(minimum, expected * share);
    }

    private static NutritionLabelReading ToReading(Column serving, decimal servingGrams)
    {
        var rows = new List<NutritionLabelRow> { new("Serving Size", Format(servingGrams) + "g") };
        var kcal = serving.Kcal!.Value;
        rows.Add(new("Energy", serving.Kj is { } kj ? $"{Format(kj)}kJ / {Format(kcal)}kcal" : $"{Format(kcal)}kcal"));
        foreach (var (nutrient, label) in Published)
        {
            if (serving.Grams.TryGetValue(nutrient, out var amount))
                rows.Add(new(label, amount.Text));
        }

        decimal? Exact(Nutrient n) => serving.Grams.TryGetValue(n, out var a) && a.Low == a.High ? a.Low : null;

        return new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = "Nutrition Information",
            ServingSizeGrams = servingGrams,
            Calories = kcal,
            ProteinGrams = Exact(Nutrient.Protein),
            CarbohydrateGrams = Exact(Nutrient.Carbohydrate),
            FatGrams = Exact(Nutrient.Fat),
            FiberGrams = Exact(Nutrient.Fibre),
            Rows = rows,
        };
    }

    private static Header? HeaderOf(List<List<string>> cells)
    {
        int? per100 = null;
        int? servingColumn = null;
        decimal grams = 0;
        for (var i = 1; i < cells.Count; i++)
        {
            var text = string.Join(" ", cells[i]);
            if (per100 is null && Per100Regex().IsMatch(text))
            {
                per100 = i;
                continue;
            }

            if (servingColumn is null && ServingHeaderRegex().Match(text) is { Success: true } m && Number(m.Groups[1].Value) is { } g)
            {
                servingColumn = i;
                grams = g;
            }
        }

        return per100 is { } a && servingColumn is { } b ? new Header(a, b, grams) : null;
    }

    private static Amount? ParseGrams(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t is "nil" or "0")
            return new Amount(0, 0, "0g");
        if (t == "trace")
            return new Amount(0, 0.5m, "Trace");

        var m = GramsRegex().Match(t);
        if (!m.Success || Number(m.Groups[2].Value) is not { } value)
            return null;

        var less = m.Groups[1].Success;
        var printed = m.Groups[2].Value.Replace(',', '.');
        var unit = m.Groups[3].Value == "mg" ? "mg" : "g";
        var grams = unit == "mg" ? value / 1000m : value;
        return new Amount(less ? 0 : grams, grams, (less ? "<" : "") + printed + unit);
    }

    /// <summary>"1,542" is a thousands separator, "1,4" a decimal comma ("0,125" too).</summary>
    private static decimal? Number(string text)
    {
        var t = ThousandsRegex().IsMatch(text) ? text.Replace(",", "") : text.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A row's cells, each as its lines ("&lt;p&gt;Fat&lt;/p&gt;&lt;p&gt;of which saturates&lt;/p&gt;" is two).</summary>
    private static List<List<string>> Cells(string rowHtml) =>
        CellRegex().Matches(rowHtml)
            .Select(c => WebUtility.HtmlDecode(TagRegex().Replace(LineBreakRegex().Replace(c.Groups[1].Value, "\n"), ""))
                .Replace(' ', ' ')
                .Split('\n')
                .Select(line => SpaceRegex().Replace(line, " ").Trim())
                .Where(line => line.Length > 0)
                .ToList())
            .ToList();

    /// <summary>Start and end of each top-level table; Myprotein nests the header and the rows in one.</summary>
    private static List<(int Start, int End)> OutermostTables(string html)
    {
        var tables = new List<(int, int)>();
        var depth = 0;
        var start = 0;
        foreach (Match tag in TableTagRegex().Matches(html))
        {
            if (tag.Value[1] != '/')
            {
                if (depth++ == 0)
                    start = tag.Index;
            }
            else if (depth > 0 && --depth == 0)
            {
                tables.Add((start, tag.Index + tag.Length));
            }
        }

        return tables;
    }

    // A row without a row inside it: Myprotein wraps whole tables in the cells of an outer table.
    [GeneratedRegex(@"<tr\b[^>]*>((?:(?!<tr\b|</tr\s*>)[\s\S])*)</tr\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LeafRowRegex();

    [GeneratedRegex(@"<t[dh]\b[^>]*>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"<table\b|</table\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex TableTagRegex();

    [GeneratedRegex(@"<br\s*/?>|</p\s*>|</div\s*>|</li\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"\bper\s*100\s*g\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Per100Regex();

    // "Per 30g", "per serving (30g)", "Per 30g Serving", "per 2 scoops (60g)".
    [GeneratedRegex(@"\bper\b[^\n]*?(\d+(?:[.,]\d+)?)\s*g\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ServingHeaderRegex();

    [GeneratedRegex(@"(\d[\d,]*(?:\.\d+)?)\s*kcal\b")]
    private static partial Regex KcalRegex();

    [GeneratedRegex(@"(\d[\d,]*(?:\.\d+)?)\s*kj\b")]
    private static partial Regex KjRegex();

    [GeneratedRegex(@"^(\d[\d,]*(?:\.\d+)?)\s*/\s*(\d[\d,]*(?:\.\d+)?)$")]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"^(\d[\d,]*(?:\.\d+)?)$")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"kj\s*/\s*kcal")]
    private static partial Regex KjKcalLabelRegex();

    [GeneratedRegex(@"^(?:total\s+)?fats?\b")]
    private static partial Regex FatLabelRegex();

    [GeneratedRegex(@"^(<|less\s+than)?\s*(\d+(?:[.,]\d+)?)\s*(g|mg)?$")]
    private static partial Regex GramsRegex();

    [GeneratedRegex(@"^[1-9]\d{0,2}(?:,\d{3})+(?:\.\d+)?$")]
    private static partial Regex ThousandsRegex();
}
