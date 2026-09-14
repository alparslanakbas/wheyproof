using IndirimTakip.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.NutritionLabels;

public sealed record NutritionLabelOutcome(
    string ImageUrl,
    string SampleProduct,
    int ProductCount,
    bool Accepted,
    string Reason,
    NutritionLabelReading? Reading,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// Reads queued label images and, when allowed, writes the nutrition table.
/// </summary>
/// <remarks>
/// <b>ONE READ PER IMAGE, NOT PER ROW.</b> Every size of a Shopify product
/// shares its images, so Nutricost's whey in 1.5, 2 and 5 lb is three rows and
/// one label. The read is applied to every row pointing at that URL.
///
/// <b>Servings per container is NOT written.</b> The panel belongs to one size
/// (Nutricost's reads "25 servings", the 2 lb tub) while the same image sits on
/// the 5 lb row; writing it there would be wrong. Serving size and the
/// per-serving amounts are the same for every size.
/// </remarks>
public sealed class NutritionLabelService(AppDbContext db, NutritionLabelReader reader)
{
    /// <summary>
    /// Label images that haven't been read at their current URL, most clicked
    /// first. <paramref name="source"/> limits them to one brand or seller.
    /// </summary>
    public async Task<List<(string Url, string SampleProduct, int ProductCount)>> QueueAsync(
        int take, string? source, CancellationToken cancellationToken)
    {
        var query = db.Products.IgnoreQueryFilters()
            .Where(p => p.NutritionLabelImageUrl != null
                && (p.NutritionLabelReadUrl == null || p.NutritionLabelReadUrl != p.NutritionLabelImageUrl));

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(p => (p.Seller ?? p.Brand!.Name) == source);

        var rows = await query
            .GroupBy(p => p.NutritionLabelImageUrl!)
            .Select(g => new
            {
                Url = g.Key,
                Clicks = g.Sum(p => p.ClickCount),
                Count = g.Count(),
                Sample = g.Min(p => p.Name),
            })
            .OrderByDescending(x => x.Clicks)
            .ThenBy(x => x.Url)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Url, r.Sample, r.Count)).ToList();
    }

    /// <summary>
    /// Reads one image. With <paramref name="write"/> false (the pilot) nothing
    /// is stored. Returns null for a transient failure, which is retried later.
    /// </summary>
    public async Task<NutritionLabelOutcome?> ProcessAsync(
        (string Url, string SampleProduct, int ProductCount) item, bool write, string? model, CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(item.Url, model, cancellationToken);
        if (result.IsTransient)
            return null;

        var verdict = result.Reading is null
            ? new NutritionLabelVerdict(false, result.Error ?? "no reading")
            : NutritionLabelValidator.Validate(result.Reading);

        if (write)
            await WriteAsync(item.Url, result.Reading, verdict, cancellationToken);

        return new NutritionLabelOutcome(
            item.Url, item.SampleProduct, item.ProductCount, verdict.Accepted, verdict.Reason,
            result.Reading, result.InputTokens, result.OutputTokens);
    }

    private async Task WriteAsync(string url, NutritionLabelReading? reading, NutritionLabelVerdict verdict, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var status = Truncate($"{(verdict.Accepted ? "accepted" : "rejected")}: {verdict.Reason}", 300);
        var rows = db.Products.IgnoreQueryFilters().Where(p => p.NutritionLabelImageUrl == url);

        if (!verdict.Accepted || reading is null)
        {
            // Marked as read so the same image isn't paid for again on every
            // run; a new image URL queues the product again.
            await rows.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.NutritionLabelReadUrl, url)
                .SetProperty(p => p.NutritionLabelStatus, status)
                .SetProperty(p => p.NutritionCheckedAt, now), cancellationToken);
            return;
        }

        var nutritionJson = NutritionParser.BuildNutritionJson(reading.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Label) && !string.IsNullOrWhiteSpace(r.Amount))
            .Select(r => (r.Label, r.Amount)));
        var protein = reading.ProteinGrams is > 0 and <= 100 ? reading.ProteinGrams : NutritionParser.ExtractProteinGrams(nutritionJson);

        await rows.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.NutritionJson, nutritionJson)
            .SetProperty(p => p.ProteinPerServingGrams, protein)
            .SetProperty(p => p.ServingSizeGrams, p => reading.ServingSizeGrams ?? p.ServingSizeGrams)
            .SetProperty(p => p.NutritionLabelReadUrl, url)
            .SetProperty(p => p.NutritionLabelStatus, status)
            .SetProperty(p => p.NutritionCheckedAt, now)
            .SetProperty(p => p.ContentUpdatedAt, now), cancellationToken);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
