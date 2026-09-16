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

    // --- Supplement Facts: named rows, no calories to check against ---

    private static ManualNutritionRequest Rows(decimal? servingSize, params (string? Label, decimal? Amount, string? Unit)[] rows) =>
        new(servingSize, null, null, null, null, null,
            rows.Select(r => new ManualNutritionRow(r.Label, r.Amount, r.Unit)).ToList());

    [Fact]
    public void A_supplement_facts_panel_needs_no_macros_and_keeps_the_typed_order()
    {
        var (reading, verdict) = ManualProductDataService.Check(Rows(5,
            ("Creatine Monohydrate", 5, "g"), ("Vitamin D3", 25, "mcg"), ("Vitamin A", 3000, "iu"), ("Probiotics", 10, "Billion CFU")));

        Assert.True(verdict.Accepted, verdict.Reason);
        Assert.Equal("Supplement Facts", reading.PanelType);
        Assert.Equal(
            new[] { ("Serving Size", "5g"), ("Creatine Monohydrate", "5g"), ("Vitamin D3", "25mcg"), ("Vitamin A", "3000 IU"), ("Probiotics", "10 billion CFU") },
            reading.Rows.Select(r => (r.Label, r.Amount)));
    }

    // A BCAA label that prints calories and macros AND the three amino acids.
    [Fact]
    public void Macros_and_other_rows_publish_together_after_the_calorie_check()
    {
        var request = new ManualNutritionRequest(10, 0, 0, 0, 0, null,
            [new("L-Leucine", 2.5m, "g"), new("L-Isoleucine", 1.25m, "g"), new("L-Valine", 1.25m, "g")]);

        var (reading, verdict) = ManualProductDataService.Check(request);

        Assert.True(verdict.Accepted, verdict.Reason);
        Assert.Equal("Nutrition Facts", reading.PanelType);
        Assert.Equal(
            new[] { "Serving Size", "Calories", "Total Fat", "Total Carbohydrate", "Protein", "L-Leucine", "L-Isoleucine", "L-Valine" },
            reading.Rows.Select(r => r.Label));
    }

    [Fact]
    public void Other_rows_do_not_excuse_a_failed_calorie_check() =>
        Assert.False(ManualProductDataService.Check(NakedWhey with { ProteinGrams = 250, OtherRows = [new("Sodium", 50, "mg")] }).Verdict.Accepted);

    [Fact]
    public void Nothing_entered_is_refused() =>
        Assert.False(ManualProductDataService.Check(Rows(null)).Verdict.Accepted);

    [Theory]
    [InlineData("", 5.0, "g")]                        // no name
    [InlineData("Caffeine", null, "mg")]              // no amount
    [InlineData("Caffeine", 0.0, "mg")]               // zero
    [InlineData("Caffeine", -200.0, "mg")]            // negative
    [InlineData("Caffeine", 200.0, "mgs")]            // unit outside the list
    [InlineData("Caffeine", 200.0, null)]             // no unit
    [InlineData("Caffeine", 2000000.0, "mcg")]        // extra zeros
    [InlineData("Protein", 25.0, "g")]                // macro typed as a free row
    [InlineData("total fat", 2.0, "g")]               // same, other casing
    [InlineData("Creatine Monohydrate", 50.0, "g")]   // more than the 5 g serving
    public void A_bad_row_is_refused_with_a_reason(string label, double? amount, string? unit)
    {
        var verdict = ManualProductDataService.Check(Rows(5, (label, (decimal?)amount, unit))).Verdict;

        Assert.False(verdict.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void The_same_row_twice_is_refused() =>
        Assert.False(ManualProductDataService.Check(Rows(null, ("Zinc", 11, "mg"), ("zinc ", 11, "mg"))).Verdict.Accepted);

    [Fact]
    public void Row_names_are_trimmed_and_inner_spaces_collapsed()
    {
        var (reading, _) = ManualProductDataService.Check(Rows(null, ("  Beta   Alanine ", 3.2m, "g")));

        Assert.Equal("Beta Alanine", Assert.Single(reading.Rows).Label);
    }

    // Without a serving size there is nothing to compare a gram amount with.
    [Fact]
    public void A_gram_row_without_a_serving_size_is_accepted() =>
        Assert.True(ManualProductDataService.Check(Rows(null, ("L-Citrulline", 6, "g"))).Verdict.Accepted);

    // The stored table keeps the same rows, spelled as checked.
    [Fact]
    public void The_stored_json_holds_every_row_in_order()
    {
        var (reading, _) = ManualProductDataService.Check(Rows(null, ("Caffeine", 200, "mg"), ("L-Theanine", 100, "mg")));

        var json = NutritionParser.BuildNutritionJson(reading.Rows.Select(r => (r.Label, r.Amount)));

        Assert.Equal("""{"Caffeine":"200mg","L-Theanine":"100mg"}""", json);
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
