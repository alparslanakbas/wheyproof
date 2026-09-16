namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>What the reader copied from one label image. Null = not printed.</summary>
public sealed record NutritionLabelReading
{
    public bool IsNutritionLabel { get; init; }
    /// <summary>"Nutrition Facts" or "Supplement Facts", as printed.</summary>
    public string? PanelType { get; init; }
    public decimal? ServingSizeGrams { get; init; }
    public decimal? Calories { get; init; }
    public decimal? ProteinGrams { get; init; }
    public decimal? CarbohydrateGrams { get; init; }
    public decimal? FiberGrams { get; init; }
    public decimal? SugarAlcoholGrams { get; init; }
    public decimal? FatGrams { get; init; }
    /// <summary>Every printed row, label and amount exactly as on the panel.</summary>
    public List<NutritionLabelRow> Rows { get; init; } = [];
    /// <summary>
    /// Supplement Facts rows that two OCR passes read identically, each with a known
    /// ingredient name and the panel complete (<see cref="SupplementFactsText"/>).
    /// Stands in for the calorie check such panels can't have. Never read from
    /// JSON: a model's answer must not be able to claim it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RowsCrossChecked { get; init; }
}

public sealed record NutritionLabelRow(string Label, string Amount);

public sealed record NutritionLabelVerdict(bool Accepted, string Reason);

/// <summary>
/// Decides whether a label reading may be published.
/// </summary>
/// <remarks>
/// <b>The label checks itself.</b> On a Nutrition Facts panel the calories follow
/// from the macros: 4 kcal per gram of protein and carbohydrate, 9 per gram of
/// fat. Nutricost whey: 4×25 + 4×3 + 9×2 = 130, printed 130; Orgain pouch:
/// 4×4 + 4×14 + 9×2 = 90, printed 90. A digit read wrong in a way that matters
/// (25 g read as 75 g) breaks that sum, and the reading is rejected instead of
/// silently published.
///
/// <b>What it can't catch.</b> A small misread (25 g as 28 g) stays within
/// label rounding and passes. Supplement Facts panels (creatine, capsules) print
/// no calories, so there is nothing to sum; they are accepted and marked as
/// unchecked, so the difference stays visible in the status.
///
/// <b>Fiber and sugar alcohols.</b> They count as carbohydrate but carry fewer
/// calories (erythritol ~0). A protein bar with 14 g of fiber would fail a
/// plain 4-4-9 sum while its label is right, so the expected calories are a
/// RANGE: those grams at 0 kcal up to 4 kcal.
/// </remarks>
public static class NutritionLabelValidator
{
    /// <param name="requireCalorieCheck">
    /// True for the free OCR engine: only a reading whose calories were checked
    /// against its macros may be published.
    /// </param>
    public static NutritionLabelVerdict Validate(NutritionLabelReading reading, bool requireCalorieCheck = false)
    {
        if (!reading.IsNutritionLabel)
            return Reject("the image is not a legible facts panel");

        // "rows": null in the model's JSON overrides the empty default.
        if (reading.Rows is null || !reading.Rows.Any(r => r.Amount?.Any(char.IsDigit) == true))
            return Reject("no row with an amount");

        if (reading.ProteinGrams is < 0 or > 100)
            return Reject($"protein {reading.ProteinGrams} g is out of range");

        var macros = (reading.ProteinGrams ?? 0) + (reading.CarbohydrateGrams ?? 0) + (reading.FatGrams ?? 0);
        if (reading.ServingSizeGrams is > 0 && macros > reading.ServingSizeGrams * 1.05m + 1)
            return Reject($"macros ({macros} g) exceed the serving size ({reading.ServingSizeGrams} g)");

        var isNutritionPanel = reading.PanelType?.Contains("nutrition", StringComparison.OrdinalIgnoreCase) == true;

        if (reading is { Calories: { } calories, ProteinGrams: { } protein, CarbohydrateGrams: { } carbs, FatGrams: { } fat })
        {
            var lowCalorieCarbs = Math.Min(carbs, (reading.FiberGrams ?? 0) + (reading.SugarAlcoholGrams ?? 0));
            var upper = 4 * protein + 4 * carbs + 9 * fat;
            var lower = upper - 4 * lowCalorieCarbs;
            // Label rounding: calories to the nearest 5 or 10, each macro to the
            // nearest gram, which adds up to roughly ±15 kcal.
            var tolerance = Math.Max(15m, calories * 0.1m);

            if (calories < lower - tolerance || calories > upper + tolerance)
                return Reject($"calories {calories} don't match the macros (expected {lower:0}-{upper:0})");

            return new NutritionLabelVerdict(true, "calorie check passed");
        }

        if (isNutritionPanel)
            return Reject("a Nutrition Facts panel without readable calories and macros");

        if (reading.RowsCrossChecked)
            return new NutritionLabelVerdict(true, "supplement facts rows agreed across OCR passes");

        if (requireCalorieCheck)
            return Reject("supplement facts can't be calorie-checked; this engine publishes checked panels only");

        return new NutritionLabelVerdict(true, "no calorie check (supplement facts)");
    }

    private static NutritionLabelVerdict Reject(string reason) => new(false, reason);
}
