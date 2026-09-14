using IndirimTakip.Infrastructure.NutritionLabels;

namespace IndirimTakip.Infrastructure.Tests;

public class NutritionLabelImagePickerTests
{
    // Real file names from the 2026-09-14 survey.
    [Theory]
    [InlineData("https://cdn.shopify.com/s/files/1/0222/4128/0074/files/NTC_WPC_Chocolate_2LB_2750CC_SFP_Square_40c8caf8.jpg?v=1")]
    [InlineData("https://cdn.shopify.com/s/files/1/0074/0832/0621/files/KidsSmoothiePouchMangoPeach06_015000867805_44509917_NFP_cb5c56c3.webp")]
    [InlineData("https://cdn.shopify.com/s/files/1/0645/6465/files/creatine-capsules-nutrition-facts.jpg")]
    [InlineData("https://cdn.shopify.com/s/files/1/0507/9565/files/promix-liposomal-creatine_supplement-facts.png")]
    [InlineData("https://cdn.shopify.com/s/files/1/0114/8869/0276/files/Conventional-Pea-Protein-Powder-Powder-1kg-Label-V006.jpg")]
    [InlineData("https://cdn.shopify.com/s/files/1/0471/3332/7519/files/ISOFIT_SFP_70_VB_optimized.png")]
    public void Named_label_images_are_recognised(string url) =>
        Assert.True(NutritionLabelImagePicker.IsLabelImage(url));

    [Theory]
    // The brand name contains "nutrition"; it's a product photo.
    [InlineData("https://cdn.shopify.com/s/files/1/1463/8084/files/qst-007653-quest-nutrition-peppermint-bark-protein-bar_-21g-complete-protein_1.png")]
    // A front label shows the tub, not the facts panel.
    [InlineData("https://cdn.shopify.com/s/files/1/0866/7664/files/TL-Label-265_PRTD_SL_Chocolate_2_11_FRONT_DTC.png")]
    [InlineData("https://cdn.shopify.com/s/files/1/0222/files/whey-chocolate-lifestyle.jpg")]
    // "labelled"/"flabel" must not match as a word.
    [InlineData("https://cdn.shopify.com/s/files/1/0222/files/clean-labelled-tub.jpg")]
    public void Other_images_are_not_labels(string url) =>
        Assert.False(NutritionLabelImagePicker.IsLabelImage(url));

    [Fact]
    public void Picks_the_first_label_after_product_photos()
    {
        var picked = NutritionLabelImagePicker.Pick([
            "https://cdn.shopify.com/files/tub-front.jpg",
            "https://cdn.shopify.com/files/whey-SFP.jpg",
            "https://cdn.shopify.com/files/other-nutrition-facts.jpg",
        ]);

        Assert.Equal("https://cdn.shopify.com/files/whey-SFP.jpg", picked);
    }

    [Fact]
    public void No_named_label_picks_nothing() =>
        Assert.Null(NutritionLabelImagePicker.Pick(["https://cdn.shopify.com/files/a.jpg", "https://cdn.shopify.com/files/b.jpg"]));

    [Fact]
    public void Shopify_images_are_read_at_a_fixed_width() =>
        Assert.Equal(
            "https://cdn.shopify.com/files/whey-SFP.jpg?width=1200",
            NutritionLabelImagePicker.ForReading("https://cdn.shopify.com/files/whey-SFP.jpg?v=17", 1200));
}

public class NutritionLabelValidatorTests
{
    private static readonly List<NutritionLabelRow> SomeRows = [new("Protein", "25g")];

    // Nutricost whey, read from its label image: 4x25 + 4x3 + 9x2 = 130.
    private static readonly NutritionLabelReading Nutricost = new()
    {
        IsNutritionLabel = true,
        PanelType = "Nutrition Facts",
        ServingSizeGrams = 36,
        Calories = 130,
        ProteinGrams = 25,
        CarbohydrateGrams = 3,
        FiberGrams = 1,
        FatGrams = 2,
        Rows = SomeRows,
    };

    [Fact]
    public void A_label_whose_calories_match_its_macros_is_accepted() =>
        Assert.True(NutritionLabelValidator.Validate(Nutricost).Accepted);

    [Fact]
    public void A_misread_that_breaks_the_calorie_sum_is_rejected()
    {
        var verdict = NutritionLabelValidator.Validate(Nutricost with { ProteinGrams = 75 });

        Assert.False(verdict.Accepted);
    }

    // Quest-style bar: 14 g fiber inside 22 g carbs. A plain 4-4-9 sum gives 235
    // for a correct label printing 180.
    [Fact]
    public void Fiber_counts_as_low_calorie_carbohydrate() =>
        Assert.True(NutritionLabelValidator.Validate(new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = "Nutrition Facts",
            ServingSizeGrams = 60,
            Calories = 180,
            ProteinGrams = 21,
            CarbohydrateGrams = 22,
            FiberGrams = 14,
            FatGrams = 7,
            Rows = SomeRows,
        }).Accepted);

    [Fact]
    public void Macros_heavier_than_the_serving_are_rejected() =>
        Assert.False(NutritionLabelValidator.Validate(Nutricost with { ServingSizeGrams = 3.6m }).Accepted);

    [Fact]
    public void An_image_that_is_not_a_label_is_rejected() =>
        Assert.False(NutritionLabelValidator.Validate(new NutritionLabelReading { IsNutritionLabel = false }).Accepted);

    [Fact]
    public void A_nutrition_panel_without_calories_is_rejected() =>
        Assert.False(NutritionLabelValidator.Validate(Nutricost with { Calories = null }).Accepted);

    // Naked creatine capsules: no calories on a Supplement Facts panel, so the
    // reading is accepted but says it wasn't checked.
    [Fact]
    public void Supplement_facts_are_accepted_as_unchecked()
    {
        var verdict = NutritionLabelValidator.Validate(new NutritionLabelReading
        {
            IsNutritionLabel = true,
            PanelType = "Supplement Facts",
            Rows = [new("Serving Size", "2 capsules"), new("Creatine Monohydrate", "2.5g")],
        });

        Assert.True(verdict.Accepted);
        Assert.Contains("no calorie check", verdict.Reason);
    }
}

public class NutritionLabelReaderParseTests
{
    [Fact]
    public void Reads_json_wrapped_in_a_code_fence()
    {
        var reading = NutritionLabelReader.ParseReading("""
            ```json
            {"isNutritionLabel": true, "panelType": "Nutrition Facts", "calories": "130", "proteinGrams": 25,
             "rows": [{"label": "Protein", "amount": "25g"}]}
            ```
            """);

        Assert.NotNull(reading);
        Assert.Equal(130, reading.Calories);
        Assert.Equal("25g", Assert.Single(reading.Rows).Amount);
    }

    [Fact]
    public void Text_without_json_gives_no_reading() =>
        Assert.Null(NutritionLabelReader.ParseReading("I can't read this image."));
}
