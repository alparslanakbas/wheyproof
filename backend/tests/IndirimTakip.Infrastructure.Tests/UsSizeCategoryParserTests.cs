using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// The strings are real US store names and variant labels collected on
// 2026-09-11, not invented examples.
public class UsSizeCategoryParserTests
{
    [Theory]
    [InlineData("Nutricost Whey Protein Concentrate Powder - 5 lbs", "5 lb")]
    [InlineData("Nutricost Whey Protein Concentrate Powder - 1.5 lb", "1.5 lb")]
    [InlineData("Double Chocolate Whey Protein Powder 2LB | Naked Whey - 2LB", "2 lb")]
    [InlineData("Nutritional Yeast Flakes - Flakes / 250 Grams (8.8 oz)", "250 g")]
    [InlineData("Hydrolyzed Keratin Powder - Powder / 1 Kilogram (2.2 lbs)", "1 kg")]
    [InlineData("Micronised Creatine Powder - 634 g (186 servings)", "634 g")]
    [InlineData("Pre Workout Supplement | Naked Energy - 240g", "240 g")]
    [InlineData("Nutricost Organic Turmeric Powder - 8 OZ", "8 oz")]
    [InlineData("Bulk Pre-Workout - 30 Servings", "30 servings")]
    [InlineData("Zinc Orotate Capsules - Capsule / 240 Capsules", "240 capsules")]
    [InlineData("Horse Chestnut Extract Capsules - Capsule / 240 Veg Capsules", "240 capsules")]
    [InlineData("Magnesium Glycinate 400 mg 120 Capsules", "120 capsules")]
    public void Extracts_package_size_from_us_names(string name, string expected)
    {
        Assert.Equal(expected, ProductAttributeParser.ExtractSize(name));
    }

    // Exact avoirdupois definitions: a wrong factor here would skew every
    // per-gram price without any visible error.
    [Theory]
    [InlineData("5 lb", "2267.96185")]
    [InlineData("1.5 lb", "680.388555")]
    [InlineData("8 oz", "226.796185")]
    [InlineData("1 kg", "1000")]
    [InlineData("250 g", "250")]
    public void Converts_weights_to_grams(string size, string grams)
    {
        Assert.Equal(decimal.Parse(grams, System.Globalization.CultureInfo.InvariantCulture), ProductAttributeParser.ToGrams(size));
    }

    [Theory]
    [InlineData("30 servings")]
    [InlineData("240 capsules")]
    [InlineData("")]
    [InlineData(null)]
    public void Counts_have_no_weight(string? size)
    {
        Assert.Null(ProductAttributeParser.ToGrams(size));
    }

    [Theory]
    [InlineData("30 Servings", 30)]
    [InlineData("634 g (186 servings)", 186)]
    [InlineData("Powder / 60 Servings", 60)]
    public void Reads_declared_servings(string text, int expected)
    {
        Assert.Equal(expected, ProductAttributeParser.ExtractServings(text));
    }

    [Theory]
    [InlineData("Grass-Fed Whey Isolate Protein Powder")]
    [InlineData(null)]
    public void Invents_no_servings(string? text)
    {
        Assert.Null(ProductAttributeParser.ExtractServings(text));
    }

    [Theory]
    [InlineData("Bulk Pre-Workout", "pre-workout")]
    [InlineData("Pre Workout with Creatine", "pre-workout")]
    [InlineData("Creatine HMB", "creatine")]
    [InlineData("BCAA Glutamine", "amino-acids")]
    [InlineData("Nutricost Electrolytes", "hydration")]
    [InlineData("Carbs + Electrolytes Shot", "hydration")]
    [InlineData("L-Carnitine Liquid", "fat-burners")]
    [InlineData("Double Chocolate Mass Gainer Protein Supplement", "mass-gainers")]
    [InlineData("Elev8 Creamy Rice Georgia Peach Carb Powders", "mass-gainers")]
    [InlineData("Grass-Fed Whey Isolate Protein Powder", "protein-powder")]
    [InlineData("Chocolate Chip Protein Bar", "protein-snacks")]
    [InlineData("Moringa Extract Capsules", "vitamins")]
    [InlineData("Greens", "vitamins")]
    public void Infers_english_category(string name, string expected)
    {
        Assert.Equal(expected, ProductAttributeParser.InferCategory(name));
    }

    // Real product seen in the survey (Ascent). A substring match on "pump"
    // made it a pre-workout.
    [Fact]
    public void Pumpkin_is_not_a_pump()
    {
        Assert.Equal("protein-powder", ProductAttributeParser.InferCategory("Pumpkin Spice Iced Coffee with Protein"));
    }

    [Fact]
    public void Grass_is_not_mass()
    {
        Assert.Equal("protein-powder", ProductAttributeParser.InferCategory("Grass-Fed Whey Protein"));
    }

    // "bulk" would have turned every BulkSupplements product into a gainer.
    [Fact]
    public void Bulk_is_not_a_category_word()
    {
        Assert.Null(ProductAttributeParser.InferCategory("BulkSupplements Nutritional Yeast Flakes"));
    }
}
