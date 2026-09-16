namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>An engine that turns a label image into a <see cref="NutritionLabelReading"/>.</summary>
public interface INutritionLabelReader
{
    /// <summary>"tesseract" (free, runs on the server) or "claude" (paid API).</summary>
    string Engine { get; }

    /// <summary>Whether the engine can run right now (binary installed, key set).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// When true only calorie-checked Nutrition Facts panels may be published.
    /// The free OCR engine loses the amount column on Supplement Facts panels,
    /// and those print no calories to check against (measured 2026-09-14).
    /// Exception: Supplement Facts rows the engine cross-checked itself
    /// (<see cref="NutritionLabelReading.RowsCrossChecked"/>).
    /// </summary>
    bool RequiresCalorieCheck { get; }

    Task<NutritionLabelReadResult> ReadAsync(string imageUrl, string? model, CancellationToken cancellationToken);
}
