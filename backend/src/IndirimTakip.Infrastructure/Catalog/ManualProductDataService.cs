using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Catalog;

/// <summary>Nutrition per serving as typed into the admin panel from a brand's label.</summary>
/// <param name="OtherRows">
/// Rows beyond the macros, as printed on a Supplement Facts panel ("Creatine
/// Monohydrate 5 g", "Vitamin D3 25 mcg", "Caffeine 200 mg").
/// </param>
public sealed record ManualNutritionRequest(
    decimal? ServingSizeGrams,
    decimal? Calories,
    decimal? ProteinGrams,
    decimal? CarbohydrateGrams,
    decimal? FatGrams,
    decimal? FiberGrams,
    IReadOnlyList<ManualNutritionRow>? OtherRows = null);

/// <summary>One label row: name, amount per serving and its unit.</summary>
public sealed record ManualNutritionRow(string? Label, decimal? Amount, string? Unit);

public sealed record ManualEditResult(bool Found, bool Accepted, string? Reason = null, int RowsUpdated = 0)
{
    public static readonly ManualEditResult Missing = new(false, false);
}

/// <summary>
/// Category and nutrition entered by a person, for products no scraper could
/// read: a label only published as an image OCR can't read, or a name like
/// "Animal Pak" that says nothing about what the product is.
/// </summary>
/// <remarks>
/// <b>Applied to every size row of the product page.</b> Sizes share the page
/// and its label, so editing one row and leaving its 2 lb and 5 lb siblings
/// empty would be half a fix.
///
/// <b>Flagged manual.</b> Scrapes rewrite Category every six hours and the label
/// reader writes nutrition when an image changes; the flags keep both away from
/// a person's values.
///
/// <b>Typed values pass the same calorie check as automatic readings.</b> A typo
/// such as 250 g protein instead of 25 breaks the 4/4/9 sum and is refused
/// with the reason, instead of going live. Supplement Facts rows have no sum to
/// check, so each one is checked on its own (unit list, above zero, a gram
/// amount no larger than the serving); see <see cref="Check"/>.
/// </remarks>
public sealed class ManualProductDataService(AppDbContext db)
{
    public async Task<ManualEditResult> SetCategoryAsync(int id, string? category, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (slug is not null && !ProductAttributeParser.CategorySlugs.Contains(slug))
            return new ManualEditResult(true, false, $"'{slug}' is not a category.");

        var product = await FindAsync(id, cancellationToken);
        if (product is null)
            return ManualEditResult.Missing;

        var now = DateTimeOffset.UtcNow;
        // Back to automatic: only the flag is cleared. The inferred category
        // returns with the next crawl; inferring it here would skip the
        // scraper's own fallbacks and could disagree with that crawl.
        var updated = slug is null
            ? await SiblingRows(product.Value).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CategoryIsManual, false), cancellationToken)
            : await SiblingRows(product.Value).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Category, slug)
                .SetProperty(p => p.CategoryIsManual, true)
                .SetProperty(p => p.ContentUpdatedAt, now), cancellationToken);

        return new ManualEditResult(true, true, RowsUpdated: updated);
    }

    public async Task<ManualEditResult> SetNutritionAsync(int id, ManualNutritionRequest request, CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);
        if (product is null)
            return ManualEditResult.Missing;

        var (reading, verdict) = Check(request);
        if (!verdict.Accepted)
            return new ManualEditResult(true, false, verdict.Reason);

        var nutritionJson = NutritionParser.BuildNutritionJson(reading.Rows.Select(r => (r.Label, r.Amount)));
        var now = DateTimeOffset.UtcNow;

        var updated = await SiblingRows(product.Value).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.NutritionJson, nutritionJson)
            .SetProperty(p => p.ProteinPerServingGrams, request.ProteinGrams)
            .SetProperty(p => p.ServingSizeGrams, p => request.ServingSizeGrams ?? p.ServingSizeGrams)
            .SetProperty(p => p.NutritionIsManual, true)
            .SetProperty(p => p.NutritionLabelStatus, "manual")
            .SetProperty(p => p.NutritionCheckedAt, now)
            .SetProperty(p => p.ContentUpdatedAt, now), cancellationToken);

        return new ManualEditResult(true, true, RowsUpdated: updated);
    }

    /// <summary>
    /// Removes the panel and hands the product back to the automatic sources:
    /// the label reader and the detail backfill both pick it up again.
    /// </summary>
    public async Task<ManualEditResult> ClearNutritionAsync(int id, CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);
        if (product is null)
            return ManualEditResult.Missing;

        var now = DateTimeOffset.UtcNow;
        var updated = await SiblingRows(product.Value).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.NutritionJson, (string?)null)
            .SetProperty(p => p.ProteinPerServingGrams, (decimal?)null)
            .SetProperty(p => p.NutritionIsManual, false)
            .SetProperty(p => p.NutritionLabelStatus, (string?)null)
            .SetProperty(p => p.NutritionLabelReadUrl, (string?)null)
            .SetProperty(p => p.NutritionCheckedAt, (DateTimeOffset?)null)
            .SetProperty(p => p.ContentUpdatedAt, now), cancellationToken);

        return new ManualEditResult(true, true, RowsUpdated: updated);
    }

    // Units a label row may use. A closed list, so "5 gr" or "200 mgs" is refused
    // with the list instead of going live in a spelling the site shows nowhere else.
    private static readonly string[] RowUnits = ["g", "mg", "mcg", "IU", "billion CFU"];

    // The macro rows have their own fields and pass the calorie check there; typed
    // again as free rows they would skip that check.
    private static readonly HashSet<string> MacroLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Serving Size", "Calories", "Total Fat", "Total Carbohydrate", "Dietary Fiber", "Protein",
    };

    private const int MaxOtherRows = 40;
    private const int MaxLabelLength = 60;

    // Largest amount any unit plausibly prints per serving (100,000 mcg biotin
    // exists; nothing on a sports label goes above it). Catches extra zeros.
    private const decimal MaxRowAmount = 100_000m;

    /// <summary>The reading typed values would publish, and whether they may.</summary>
    /// <remarks>
    /// <b>Two kinds of panel.</b> Nutrition Facts (protein powder, bars): calories,
    /// protein, carbohydrate and fat together, and the 4/4/9 calorie check. Supplement
    /// Facts (creatine, amino acids, pre-workout, vitamins): named rows with no calories
    /// to check against, so each row is checked on its own instead. Measured on
    /// 2026-09-17: creatine, amino acids, pre-workout and fat burners had 0 products
    /// with nutrition and vitamins 4 of 684, because this form only took macros.
    /// </remarks>
    public static (NutritionLabelReading Reading, NutritionLabelVerdict Verdict) Check(ManualNutritionRequest request)
    {
        var reading = new NutritionLabelReading
        {
            IsNutritionLabel = true,
            ServingSizeGrams = request.ServingSizeGrams,
            Calories = request.Calories,
            ProteinGrams = request.ProteinGrams,
            CarbohydrateGrams = request.CarbohydrateGrams,
            FatGrams = request.FatGrams,
            FiberGrams = request.FiberGrams,
        };

        decimal?[] values = [request.ServingSizeGrams, request.Calories, request.ProteinGrams, request.CarbohydrateGrams, request.FatGrams, request.FiberGrams];
        if (values.Any(v => v < 0))
            return Refuse(reading, "values can't be negative");

        var (otherRows, rowError) = CheckOtherRows(request.OtherRows ?? [], request.ServingSizeGrams);
        if (rowError is not null)
            return Refuse(reading, rowError);

        decimal?[] macros = [request.Calories, request.ProteinGrams, request.CarbohydrateGrams, request.FatGrams];
        var hasMacros = macros.Any(v => v is not null);

        if (hasMacros)
        {
            if (macros.Any(v => v is null))
                return Refuse(reading, "calories, protein, carbohydrate and fat go together: enter all four, or none for a supplement facts panel");

            var macroReading = reading with { PanelType = "Nutrition Facts", Rows = LabelTextParser.CheckedRows(reading) };
            var macroVerdict = NutritionLabelValidator.Validate(macroReading, requireCalorieCheck: true);
            if (!macroVerdict.Accepted)
                return (macroReading, macroVerdict);

            return (macroReading with { Rows = [.. macroReading.Rows, .. otherRows] }, macroVerdict);
        }

        if (otherRows.Count == 0)
            return Refuse(reading, "enter calories, protein, carbohydrate and fat, or at least one other row from the label");

        var supplementReading = reading with
        {
            PanelType = "Supplement Facts",
            Rows = [.. LabelTextParser.CheckedRows(reading), .. otherRows],
        };
        return (supplementReading, NutritionLabelValidator.Validate(supplementReading));
    }

    private static (List<NutritionLabelRow> Rows, string? Error) CheckOtherRows(
        IReadOnlyList<ManualNutritionRow> rows, decimal? servingSizeGrams)
    {
        if (rows.Count > MaxOtherRows)
            return ([], $"at most {MaxOtherRows} other rows");

        var result = new List<NutritionLabelRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var label = string.Join(' ', (row.Label ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (label.Length == 0)
                return ([], "a row has no name");
            if (label.Length > MaxLabelLength)
                return ([], $"'{label[..20]}…' is longer than {MaxLabelLength} characters");
            if (MacroLabels.Contains(label))
                return ([], $"'{label}' has its own field above; enter it there");
            if (!seen.Add(label))
                return ([], $"'{label}' is listed twice");

            if (row.Amount is not { } amount)
                return ([], $"'{label}' has no amount");
            if (amount <= 0)
                return ([], $"'{label}' must be above zero");
            if (amount > MaxRowAmount)
                return ([], $"'{label}' {amount} looks like a typo");

            var unit = RowUnits.FirstOrDefault(u => string.Equals(u, row.Unit?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (unit is null)
                return ([], $"'{label}': unit must be one of {string.Join(", ", RowUnits)}");

            // 50 g of creatine in a 5 g scoop is a slipped decimal, not a label.
            if (unit == "g" && servingSizeGrams is > 0 && amount > servingSizeGrams * 1.05m + 1)
                return ([], $"'{label}' ({amount} g) is more than the serving size ({servingSizeGrams} g)");

            result.Add(new NutritionLabelRow(label, FormatAmount(amount, unit)));
        }

        return (result, null);
    }

    // Same spelling as the macro rows ("25g"); word units keep a space ("1000 IU").
    private static string FormatAmount(decimal amount, string unit)
    {
        var number = amount.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return unit is "g" or "mg" or "mcg" ? number + unit : $"{number} {unit}";
    }

    private static (NutritionLabelReading, NutritionLabelVerdict) Refuse(NutritionLabelReading reading, string reason) =>
        (reading, new NutritionLabelVerdict(false, reason));

    private async Task<(int BrandId, string? Seller, string Url)?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.BrandId, p.Seller, p.Url })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.BrandId, row.Seller, row.Url);
    }

    // Every size row of the same product page: same brand and seller, same URL
    // without the "?variant=" part.
    private IQueryable<Product> SiblingRows((int BrandId, string? Seller, string Url) product)
    {
        var page = product.Url.Split('?', 2)[0];
        var prefix = page + "?";
        var brandId = product.BrandId;
        var seller = product.Seller;
        return db.Products.IgnoreQueryFilters()
            .Where(p => p.BrandId == brandId && p.Seller == seller && (p.Url == page || p.Url.StartsWith(prefix)));
    }
}
