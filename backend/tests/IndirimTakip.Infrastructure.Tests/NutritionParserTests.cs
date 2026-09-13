using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

public class NutritionParserTests
{
    [Fact]
    public void BuildNutritionJson_returns_null_without_meaningful_rows()
    {
        Assert.Null(NutritionParser.BuildNutritionJson([]));
        // Rows without a number aren't taken into the table (text mixed in).
        Assert.Null(NutritionParser.BuildNutritionJson([("Product description", "A great product")]));
    }

    [Fact]
    public void BuildNutritionJson_simplifies_a_repeated_label()
    {
        // Some stores give bilingual labels such as "Protein / Protein".
        var json = NutritionParser.BuildNutritionJson([("Protein / Protein", "22 g")]);

        Assert.NotNull(json);
        Assert.Contains("\"Protein\"", json);
        Assert.DoesNotContain("Protein / Protein", json);
    }

    [Fact]
    public void BuildNutritionJson_keeps_the_first_of_a_repeated_label()
    {
        var json = NutritionParser.BuildNutritionJson([("Protein", "24 g"), ("Protein", "48 %RDA")]);

        Assert.NotNull(json);
        Assert.Contains("24 g", json);
        Assert.DoesNotContain("48", json);
    }

    [Theory]
    [InlineData("24 g", 24)]
    [InlineData("24,5 g", 24.5)]
    [InlineData("23.8g", 23.8)]
    public void ExtractProteinGrams_reads_different_spellings(string value, decimal expected)
    {
        var json = NutritionParser.BuildNutritionJson([("Protein", value)]);

        Assert.Equal(expected, NutritionParser.ExtractProteinGrams(json));
    }

    [Fact]
    public void ExtractProteinGrams_skips_non_gram_units()
    {
        // Rows such as "Calories from protein" aren't the protein AMOUNT.
        var json = NutritionParser.BuildNutritionJson([("Energy from protein", "96 kcal")]);

        Assert.Null(NutritionParser.ExtractProteinGrams(json));
    }

    [Fact]
    public void ExtractProteinGrams_skips_product_name_rows()
    {
        // "Protein Tozu: 900 g" (protein powder) is package information, not protein per serving.
        var json = NutritionParser.BuildNutritionJson([("Protein Tozu", "900 g")]);

        Assert.Null(NutritionParser.ExtractProteinGrams(json));
    }

    [Fact]
    public void ExtractProteinGrams_rejects_a_value_outside_the_reasonable_range()
    {
        // "Protein per serving" above 100 g points to the wrong row being captured.
        var json = NutritionParser.BuildNutritionJson([("Protein", "900 g")]);

        Assert.Null(NutritionParser.ExtractProteinGrams(json));
    }

    [Fact]
    public void ExtractProteinGrams_returns_null_without_data()
    {
        Assert.Null(NutritionParser.ExtractProteinGrams(null));
        Assert.Null(NutritionParser.ExtractProteinGrams("broken json"));
        Assert.Null(NutritionParser.ExtractProteinGrams(
            NutritionParser.BuildNutritionJson([("Carbohydrate", "3 g")])));
    }
}
