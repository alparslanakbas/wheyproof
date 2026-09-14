using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Catalog;

/// <summary>Nutrition per serving as typed into the admin panel from a brand's label.</summary>
public sealed record ManualNutritionRequest(
    decimal? ServingSizeGrams,
    decimal? Calories,
    decimal? ProteinGrams,
    decimal? CarbohydrateGrams,
    decimal? FatGrams,
    decimal? FiberGrams);

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
/// with the reason, instead of going live.
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

    /// <summary>The reading typed values would publish, and whether they may.</summary>
    public static (NutritionLabelReading Reading, NutritionLabelVerdict Verdict) Check(ManualNutritionRequest request)
    {
        var reading = new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = "Nutrition Facts",
            ServingSizeGrams = request.ServingSizeGrams,
            Calories = request.Calories,
            ProteinGrams = request.ProteinGrams,
            CarbohydrateGrams = request.CarbohydrateGrams,
            FatGrams = request.FatGrams,
            FiberGrams = request.FiberGrams,
        };
        reading = reading with { Rows = LabelTextParser.CheckedRows(reading) };

        decimal?[] values = [request.ServingSizeGrams, request.Calories, request.ProteinGrams, request.CarbohydrateGrams, request.FatGrams, request.FiberGrams];
        if (values.Any(v => v < 0))
            return (reading, new NutritionLabelVerdict(false, "values can't be negative"));

        if (request.Calories is null || request.ProteinGrams is null || request.CarbohydrateGrams is null || request.FatGrams is null)
            return (reading, new NutritionLabelVerdict(false, "calories, protein, carbohydrate and fat are all required"));

        return (reading, NutritionLabelValidator.Validate(reading, requireCalorieCheck: true));
    }

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
