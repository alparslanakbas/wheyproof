using System.Text.Json;
using IndirimTakip.Infrastructure.NutritionLabels;

namespace IndirimTakip.Infrastructure.Tests;

// OCR text below is Tesseract 5.3.4 output on real label images (2026-09-17),
// trimmed to the panel. Names in comments are the products the labels belong to.
public class SupplementFactsTextTests
{
    // Orgain Creatine, psm 3.
    private const string OrgainCreatine = """
        Supplement Facts
        Serving Size 1 Scoop (5.7g)
        Servings Per Container About 70
        Amount % Daily
        Per Serving Value
        Sodium 30mg 1%
        Creatine Monohydrate 5g *
        *Daily Value not established.
        OTHER INGREDIENTS: NATURAL FLAVORS, CITRIC ACID, REB M (STEVIA EXTRACT), SEA SALT.
        """;

    [Fact]
    public void Two_passes_reading_the_same_complete_panel_publish_it()
    {
        var (reading, reason) = SupplementFactsText.Agree([OrgainCreatine, OrgainCreatine]);

        Assert.NotNull(reading);
        Assert.True(reading.RowsCrossChecked);
        Assert.Equal("Supplement Facts", reading.PanelType);
        Assert.Equal(5.7m, reading.ServingSizeGrams);
        Assert.Equal(
            new[] { ("Serving Size", "5.7g"), ("Sodium", "30mg"), ("Creatine Monohydrate", "5g") },
            reading.Rows.Select(r => (r.Label, r.Amount)));
        Assert.True(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted, reason);
    }

    // Transparent Labs BCAA Glutamine: psm 6 dropped the L-Valine line without a trace.
    [Fact]
    public void A_pass_that_dropped_a_row_blocks_the_panel()
    {
        const string psm3 = """
            Supplement Facts
            Serving Size: 1 Scoop (19.60g)
            Vitamin C (as Ascorbic Acid) 1,500mg 1,667%
            L-Leucine 4,000mg **
            L-Isoleucine 2,000mg **
            L-Valine 2,000mg **
            ** Daily Values not established.
            """;
        var psm6 = psm3.Replace("L-Valine 2,000mg **\n", "");

        var (reading, reason) = SupplementFactsText.Agree([psm3, psm6]);

        Assert.Null(reading);
        Assert.Contains("different rows", reason);
    }

    [Fact]
    public void One_pass_alone_is_never_enough() =>
        Assert.Null(SupplementFactsText.Agree([OrgainCreatine]).Reading);

    [Theory]
    // Naked EAA: "7 g **" read as "79". Nutricost Leucine: "5g" read as "59".
    [InlineData("Palatinose Isomaltulose 79 **", "no readable unit")]
    [InlineData("L-Leucine (instantized) 59", "no readable unit")]
    // "Iron" read as "lron": the lower-case start already marks it as a continued line.
    [InlineData("lron 0.5mg 3%", "continued line")]
    // Misreads repeated identically in every pass: only the name list catches them.
    [InlineData("Vitamin Be (as pyridoxal-5'-phosphate monohydrate) 25mg 1471%", "unknown row name")]
    [InlineData("L-Ilsoleucine 585 mg **", "unknown row name")]
    // Transparent Labs Bulk Black: this IS the caffeine row, not a note.
    [InlineData("(Purcaf® Organic Caffeine (from 325 mg", "continued line")]
    // PANDA Aminos: the second half of a wrapped name.
    [InlineData("(Calci-K®), Aquamin® S sea minerals, calcium silicate) 155mg 12%", "continued line")]
    [InlineData("citrate (Calci-K®), disodium phosphate) 87mg 7%", "continued line")]
    public void A_line_that_cannot_be_trusted_rejects_the_whole_panel(string line, string expected)
    {
        var text = $"""
            Supplement Facts
            Serving Size: 1 Scoop (20g)
            Amount Per Serving
            Sodium 50 mg 2%*
            {line}
            **Daily Value not established.
            """;

        var panel = SupplementFactsText.Read(text);

        Assert.Empty(panel.Rows);
        Assert.Contains(expected, panel.Problem);
    }

    // RAW Creatine + HMB, psm 6: "2g **" read as "29g" in a 15 g scoop.
    [Fact]
    public void Grams_above_the_serving_are_refused_even_when_passes_agree()
    {
        const string text = """
            Supplement Facts
            Serving Size 1 Scoop (15g)
            Creatine Monohydrate 5g **
            Taurine 29g **
            **Percent Daily Value not established.
            """;

        var (reading, reason) = SupplementFactsText.Agree([text, text]);

        Assert.Null(reading);
        Assert.Contains("more than the 15 g serving", reason);
    }

    // Clean Simple Eats: the "5g CREATINE" callout beside the panel lands among its lines;
    // "<1g" is not an amount; the "†" mark reads as "t".
    [Fact]
    public void Callouts_less_than_amounts_and_dagger_marks_are_handled()
    {
        const string text = """
            Supplement Facts
            Serving Size 1 Scoop (12.1g) 5g CREATINE
            Servings Per Container 30
            Amount Per Serving % Daily Value*
            Total Carbohydrate <1g <1%*
            Creatine Monohydrate 5,000mg t
            5g GLUTAMINE
            L-Glutamine 5gt
            Total Sugars 0g
            *Percent Daily Values based on a 2,000 calorie diet.
            """;

        var panel = SupplementFactsText.Read(text);

        Assert.Null(panel.Problem);
        Assert.Equal(
            new[] { ("Creatine Monohydrate", "5000mg"), ("L-Glutamine", "5g") },
            panel.Rows.Select(r => (r.Label, r.AmountText)));
    }

    [Fact]
    public void Noise_before_a_macro_line_is_skipped_but_a_real_word_is_not()
    {
        const string noisy = """
            Supplement Facts
            Serving Size 1 Scoop (15g)
            SW) Calories 5
            Taurine 2g **
            **Daily Value not established.
            """;
        Assert.Null(SupplementFactsText.Read(noisy).Problem);

        // "Whey Protein" must not be taken for the protein field and silently dropped.
        var panel = SupplementFactsText.Read(noisy.Replace("Taurine 2g **", "Whey Protein 20g"));
        Assert.Contains("Whey Protein", panel.Problem);
    }

    [Fact]
    public void The_panel_opens_at_the_serving_size_when_the_header_was_lost() =>
        Assert.Null(SupplementFactsText.Read(OrgainCreatine.Replace("Supplement Facts\n", "")).Problem);

    [Fact]
    public void A_nutrition_facts_panel_is_not_read_here() =>
        Assert.NotNull(SupplementFactsText.Read("Nutrition Facts\nServing Size 7g\nSodium 5mg\n").Problem);

    // "RAW CreaSol SSAT (Stabilized Tyrosol)": the known name is in the note; the
    // short name is published. Ratios such as "2:1:1" are not unread amounts.
    [Fact]
    public void Notes_can_carry_the_known_name_and_ratios_are_not_amounts()
    {
        const string text = """
            Supplement Facts
            Serving Size: 1 Scoop (13g)
            CreaSol™ SSAT (Stabilized Tyrosol) 200mg **
            Amino Acids Blend 5,000mg **
            2:1:1 BCAA (Instantized) (L-Leucine, L-Isoleucine, L-Valine),
            ** Daily Value not established.
            """;

        var panel = SupplementFactsText.Read(text);

        Assert.Null(panel.Problem);
        Assert.Equal(new[] { "CreaSol SSAT", "Amino Acids Blend" }, panel.Rows.Select(r => r.Label));
    }

    [Theory]
    [InlineData("Vitamin B-6", true)]
    [InlineData("CarnoSyn® Beta-Alanine", true)]
    [InlineData("Purcaf® Organic Caffeine", true)]
    [InlineData("L–Tyrosine", true)]
    [InlineData("Explosive Energy & Focus Complex", true)]
    [InlineData("Vitamin Be", false)]
    [InlineData("lron", false)]
    [InlineData("L-Ilsoleucine", false)]
    [InlineData("9ea Salt", true)] // "salt" is known; the unit check caught this row on the real label
    [InlineData("Anabolic Cell Volumizer", false)]
    public void Ingredient_names(string name, bool known) =>
        Assert.Equal(known, SupplementIngredientNames.IsKnown(name));

    // A model's JSON answer must not be able to skip the checks.
    [Fact]
    public void Cross_checked_cannot_be_set_from_json()
    {
        var reading = JsonSerializer.Deserialize<NutritionLabelReading>(
            """{"isNutritionLabel":true,"panelType":"Supplement Facts","rows":[{"label":"Caffeine","amount":"200mg"}],"RowsCrossChecked":true,"rowsCrossChecked":true}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.False(reading.RowsCrossChecked);
        Assert.False(NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted);
    }
}
