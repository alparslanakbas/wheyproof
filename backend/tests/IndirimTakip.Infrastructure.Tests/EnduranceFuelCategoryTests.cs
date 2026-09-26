using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// "caffeine" is a pre-workout word, so caffeinated in-race fuel landed next to
/// the stims (2026-09-26). The names below are real catalog rows.
/// </summary>
public class EnduranceFuelCategoryTests
{
    /// <summary>
    /// Already live before the fix: Veloforte's caffeine gels sat on the UK
    /// pre-workout page. There's no gel category, so they stay uncategorised.
    /// </summary>
    [Theory]
    [InlineData("Desto - Natural Energy Gel with Caffeine - 12")]
    [InlineData("Doppio - Natural Energy Gel with Caffeine - 24")]
    [InlineData("Sis Cola Gel 75mg Caffeine 60ml")]
    [InlineData("226ers High Fructose Cherry Energy Gel 160mg Caffeine 80g")]
    [InlineData("Enervit Carbo Gel C2:1PRO - Cola with caffeine - 60 ml")]
    public void Caffeinated_gel_is_not_a_pre_workout(string name)
    {
        Assert.NotEqual("pre-workout", ProductAttributeParser.InferCategory(name));
    }

    [Theory]
    [InlineData("Attivo Electrolyte Powder with Caffeine - 12")]
    [InlineData("226ERS SUB9 PRO Caffeine Mineral Salts 100 Capsules")]
    [InlineData("Mineral Salts PRO SUB9 Caffeine 226ERS 1g X 2 UND")]
    [InlineData("Mineral Salts SUB9 226ERS 2 Units x 1gr.")]            // was vitamins ("mineral")
    [InlineData("226ERS Sea Water 20ml Mineral Salts Drink")]
    [InlineData("Gold Nutrition Mineral Salts Electrolytes 60 Capsules")]
    public void Electrolyte_and_mineral_salt_products_are_hydration(string name)
    {
        Assert.Equal("hydration", ProductAttributeParser.InferCategory(name));
    }

    /// <summary>
    /// The guard: an explicit "pre-workout" wins over the fuel form, and "shot" is
    /// not a fuel word. These are real UK rows that must not move.
    /// </summary>
    [Theory]
    [InlineData("Warrior Rage Pre-Workout Energy Shot - (12x 60ml)")]
    [InlineData("Gold Standard Pre-Workout Shot - 60ml (175mg caffeine) - 1 bottle x 60 ml")]
    [InlineData("Energy Pre-Workout Shots Cherry Fizz - BOX OF 12")]
    [InlineData("Dope Shot Pre-Workout - 12 x 60ml")]
    [InlineData("Forzagen CREA-LADE Premium Creatine + Electrolytes Pre-Workout - 35 Servings")]
    [InlineData("HydroPrime® Glycerol - 80 Servings")]
    [InlineData("Caffeine and Taurine Nutrinovex 100 mg/480 mg")]
    public void Real_pre_workouts_stay(string name)
    {
        Assert.Equal("pre-workout", ProductAttributeParser.InferCategory(name));
    }

    /// <summary>A fuel form only skips the pre-workout family; the others still decide.</summary>
    [Fact]
    public void Gel_with_electrolytes_is_still_hydration()
    {
        Assert.Equal("hydration", ProductAttributeParser.InferCategory("Sis Go Energy + Electrolyte Raspberry Gel 60ml"));
    }
}
