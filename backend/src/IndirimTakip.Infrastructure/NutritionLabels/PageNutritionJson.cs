using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Reads a nutrition panel a store embeds as JSON in its product page.
/// </summary>
/// <remarks>
/// <b>Naked Nutrition, measured 2026-09-15 (14 products):</b> each page carries
/// a <c>"nutrition": { "calorie": "160", "protein": "25", "fat": "2.5",
/// "carbohydrate": "11", "serving_size": "2 Scoops (43g)" }</c> object.
///
/// <b>Only the object this product owns.</b> Pages embed other products'
/// content too, so an object is used only when this product's handle appears
/// directly before it (<c>"handle":"double-chocolate-whey-protein-2lb"</c>).
///
/// <b>Objects marked <c>"enable": "false"</c> are skipped.</b> In the sample every
/// incomplete or wrong object carried that flag: a collagen without fat, an
/// electrolyte without protein, colostrum as "&lt;1", and a creatine CAPSULES
/// page whose object described "1 Stick (5g)", another product's data. Complete
/// protein powders had no flag. The store isn't showing those panels; neither
/// do we.
///
/// The reading goes through the same calorie check as label images, so a
/// value like "&lt;1" (not a number) simply leaves the panel unpublished.
/// </remarks>
internal static partial class PageNutritionJson
{
    // How far before the object the owning handle may appear.
    private const int OwnerWindow = 600;

    public static NutritionLabelReading? Read(string html, string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return null;

        var ownerPattern = new Regex("\"handle\"\\s*:\\s*\"" + Regex.Escape(handle) + "\"");

        foreach (Match start in ObjectStartRegex().Matches(html))
        {
            var before = html.Substring(Math.Max(0, start.Index - OwnerWindow), Math.Min(OwnerWindow, start.Index));
            if (!ownerPattern.IsMatch(before))
                continue;

            var json = BalancedObject(html, start.Index + start.Length - 1);
            if (json is null)
                continue;

            var reading = ToReading(json);
            if (reading is not null && NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted)
                return reading;
        }

        return null;
    }

    internal static NutritionLabelReading? ToReading(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("enable", out var enable)
                && (enable.ValueKind == JsonValueKind.False
                    || (enable.ValueKind == JsonValueKind.String && enable.GetString()?.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) == true)))
            {
                return null;
            }

            var reading = new NutritionLabelReading
            {
                IsNutritionLabel = true,
                PanelType = "Nutrition Facts",
                ServingSizeGrams = ServingGrams(Text(root, "serving_size") ?? Text(root, "servingSize")),
                Calories = Number(root, "calorie"),
                ProteinGrams = Number(root, "protein"),
                FatGrams = Number(root, "fat"),
                CarbohydrateGrams = Number(root, "carbohydrate"),
                FiberGrams = Number(root, "fiber"),
            };

            return reading with { Rows = LabelTextParser.CheckedRows(reading) };
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // "25", "2.5" or 25; anything else ("<1", "", "-") is not a number here.
    private static decimal? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n))
            return n;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString()?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    // "2 Scoops (43g)" -> 43.
    private static decimal? ServingGrams(string? servingSize)
    {
        if (servingSize is null)
            return null;
        var match = ServingGramsRegex().Match(servingSize);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var grams)
            ? grams
            : null;
    }

    // The object that opens at `openBrace`, respecting strings so a brace inside
    // an ingredient list doesn't end it early.
    private static string? BalancedObject(string text, int openBrace)
    {
        var depth = 0;
        var inString = false;
        for (var i = openBrace; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text.Substring(openBrace, i - openBrace + 1);
        }
        return null;
    }

    [GeneratedRegex(@"""nutrition""\s*:\s*\{")]
    private static partial Regex ObjectStartRegex();

    [GeneratedRegex(@"\((\d+(?:\.\d+)?)\s*g\)", RegexOptions.IgnoreCase)]
    private static partial Regex ServingGramsRegex();
}
