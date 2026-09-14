using IndirimTakip.Infrastructure.Catalog;
using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// Values typed in the admin panel go live only if they pass the same check as
// automatic readings.
public class ManualProductDataTests
{
    // Naked whey, as printed on its label: 4x25 + 4x11 + 9x2.5 = 166 vs 160.
    private static readonly ManualNutritionRequest NakedWhey = new(43, 160, 25, 11, 2.5m, 3);

    [Fact]
    public void Label_values_are_accepted_and_become_the_checked_rows()
    {
        var (reading, verdict) = ManualProductDataService.Check(NakedWhey);

        Assert.True(verdict.Accepted);
        Assert.Equal(
            new[] { "Serving Size", "Calories", "Total Fat", "Total Carbohydrate", "Dietary Fiber", "Protein" },
            reading.Rows.Select(r => r.Label));
    }

    // 250 g protein typed instead of 25.
    [Fact]
    public void A_typo_that_breaks_the_calorie_sum_is_refused_with_a_reason()
    {
        var (_, verdict) = ManualProductDataService.Check(NakedWhey with { ProteinGrams = 250 });

        Assert.False(verdict.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void Negative_values_are_refused() =>
        Assert.False(ManualProductDataService.Check(NakedWhey with { FiberGrams = -1 }).Verdict.Accepted);

    [Theory]
    [InlineData(null, 25.0, 11.0, 2.5)]
    [InlineData(160.0, null, 11.0, 2.5)]
    [InlineData(160.0, 25.0, null, 2.5)]
    [InlineData(160.0, 25.0, 11.0, null)]
    public void Calories_and_all_three_macros_are_required(double? calories, double? protein, double? carbs, double? fat)
    {
        var request = new ManualNutritionRequest(43, (decimal?)calories, (decimal?)protein, (decimal?)carbs, (decimal?)fat, null);

        Assert.False(ManualProductDataService.Check(request).Verdict.Accepted);
    }

    // The panel's dropdown and the endpoint's check both rely on this list.
    [Fact]
    public void Category_slugs_are_the_nine_site_categories()
    {
        Assert.Equal(9, ProductAttributeParser.CategorySlugs.Count);
        Assert.Contains("protein-snacks", ProductAttributeParser.CategorySlugs);
        Assert.Contains("protein-powder", ProductAttributeParser.CategorySlugs);
    }
}
