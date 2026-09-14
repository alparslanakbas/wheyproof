using IndirimTakip.Infrastructure.NutritionLabels;

namespace IndirimTakip.Infrastructure.Tests;

public class LabelTextParserTests
{
    // OCR text shaped like Tesseract's output for Orgain's protein label
    // (2026-09-14), including its "lg"/"Og" confusions.
    private const string OrgainText = """
        Nutrition Facts
        About 18 servings per container
        Serving size 2Scoops (43g)
        Amount per serving
        Calories 160
        % Daily Value*
        Total Fat 2.5g 3%
        Saturated Fat lg 5%
        Trans Fat Og
        Sodium 390mg 17%
        Total Carbohydrate 13g 5%
        Dietary Fiber 4g 14%
        Protein 21g 33%
        *The % Daily Value (DV) tells you how much a nutrient in a serving of food contributes to a daily diet. 2,000 calories a day is used for general nutrition advice.
        Ingredients: ORGANIC PEA PROTEIN, ORGANIC BROWN RICE PROTEIN
        """;

    [Fact]
    public void Reads_the_checkable_values_of_a_nutrition_panel()
    {
        var reading = LabelTextParser.Parse(OrgainText);

        Assert.True(reading.IsNutritionLabel);
        Assert.Equal("Nutrition Facts", reading.PanelType);
        Assert.Equal(43m, reading.ServingSizeGrams);
        Assert.Equal(160m, reading.Calories);
        Assert.Equal(2.5m, reading.FatGrams);
        Assert.Equal(13m, reading.CarbohydrateGrams);
        Assert.Equal(4m, reading.FiberGrams);
        Assert.Equal(21m, reading.ProteinGrams);
        Assert.True(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted);
    }

    // Sodium or sugars have nothing to check them against, so they aren't published.
    [Fact]
    public void Only_calorie_checked_fields_become_rows() =>
        Assert.Equal(
            new[] { "Serving Size", "Calories", "Total Fat", "Total Carbohydrate", "Dietary Fiber", "Protein" },
            LabelTextParser.Parse(OrgainText).Rows.Select(r => r.Label));

    [Fact]
    public void A_percentage_after_calories_is_not_a_calorie_value() =>
        Assert.Null(LabelTextParser.Parse("Nutrition Facts\nCalories 10%\nProtein 5g").Calories);

    [Fact]
    public void Ocr_letter_zero_before_a_unit_is_read_as_zero()
    {
        var reading = LabelTextParser.Parse("Nutrition Facts\nCalories 100\nTotal Fat Og\nTotal Carbohydrate O.5g\nProtein 24g");

        Assert.Equal(0m, reading.FatGrams);
        Assert.Equal(0.5m, reading.CarbohydrateGrams);
    }

    // BulkSupplements style: upper case, serving grams in parentheses, "<1g" fiber.
    [Fact]
    public void Reads_upper_case_panels_and_skips_less_than_values()
    {
        var reading = LabelTextParser.Parse("""
            NUTRITION FACTS
            Serving Size: 4 tbsp (about 30g)
            Calories 100
            Total Fat 2.7g
            Total Carbohydrate 0g
            Dietary Fiber <1g
            Protein 21g
            """);

        Assert.Equal(30m, reading.ServingSizeGrams);
        Assert.Equal(2.7m, reading.FatGrams);
        Assert.Null(reading.FiberGrams);
        Assert.True(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted);
    }

    // When the big calorie number is lost, the footnote's "2,000 calories"
    // must not stand in for it.
    [Fact]
    public void The_footnote_is_not_taken_as_calories()
    {
        var reading = LabelTextParser.Parse("Nutrition Facts\nCalories\nProtein 21g\n2,000 calories a day is used for general nutrition advice.");

        Assert.Null(reading.Calories);
        Assert.False(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted);
    }

    [Fact]
    public void Text_without_a_facts_panel_is_not_a_label() =>
        Assert.False(LabelTextParser.Parse("Nutrition With Nothing To Hide\nVEGAN").IsNutritionLabel);

    // What OCR made of a Nutricost capsule label: the amount column is gone.
    [Fact]
    public void Supplement_facts_are_rejected_when_a_calorie_check_is_required()
    {
        var reading = LabelTextParser.Parse("Supplement Facts\nServing Size: 1 Capsule\nServings Per Container: 180\nGotu Kola Extract");

        Assert.Equal("Supplement Facts", reading.PanelType);
        Assert.False(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted);
        Assert.False(NutritionLabelValidator.Validate(reading).Accepted); // no row with an amount either
    }
}
