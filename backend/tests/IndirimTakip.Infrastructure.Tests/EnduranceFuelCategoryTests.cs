using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// "caffeine" is a pre-workout word, so caffeinated in-race fuel landed next to
/// the stims (2026-09-26). Since 2026-09-28 gels, carbohydrate drinks and
/// energy chews have their own category, "energy-gels-drinks". The names below
/// are real catalog rows.
/// </summary>
public class EnduranceFuelCategoryTests
{
    /// <summary>
    /// Already live before the first fix: Veloforte's caffeine gels sat on the
    /// UK pre-workout page. They were left uncategorised then; now they have a
    /// shelf of their own.
    /// </summary>
    [Theory]
    [InlineData("Desto - Natural Energy Gel with Caffeine - 12")]
    [InlineData("Doppio - Natural Energy Gel with Caffeine - 24")]
    [InlineData("Sis Cola Gel 75mg Caffeine 60ml")]
    [InlineData("226ers High Fructose Cherry Energy Gel 160mg Caffeine 80g")]
    [InlineData("Enervit Carbo Gel C2:1PRO - Cola with caffeine - 60 ml")]
    public void Caffeinated_gel_is_an_energy_gel(string name)
    {
        Assert.Equal("energy-gels-drinks", ProductAttributeParser.InferCategory(name));
    }

    /// <summary>
    /// Each came from a different wrong place or from no category: "carb"
    /// filed the sports drink under mass gainers, "caffeine" the chews under
    /// pre-workout, and nothing at all matched the isotonic gel or the drinks.
    /// </summary>
    [Theory]
    [InlineData("226ERS Energy Drink - 1kg Lemon")]
    [InlineData("226ERS Banana energy gel 76g. (1 unit)")]
    [InlineData("SIS Go Isotonic Tropical Gel 60ml")]
    [InlineData("Neversecond Sport Drink C90 High-Carb Mix Citrus 94g / 8 Envelopes")]
    [InlineData("Maurten Drink Mix 320 CAF100 - 80 g")]
    [InlineData("Amaro Energy Chews with Caffeine - 12")]
    [InlineData("Orange Naked Sparkling Energy / Clean Energy Drink - 12 Cans")]
    [InlineData("Endurance Gels - 12 Pack")]
    [InlineData("Additions Maurten Orange for Drink Mix (6 Units)")]
    [InlineData("Cellucor 12pk C4 Energy Shots Energy Drinks")]    // Shopify title + product type
    public void Endurance_fuel_is_energy_gels_and_drinks(string name)
    {
        Assert.Equal("energy-gels-drinks", ProductAttributeParser.InferCategory(name));
    }

    /// <summary>
    /// Look-alikes found by running the rule over every live name: capsules
    /// called "soft gels", electrolyte drink mixes, creatine chews, an energy
    /// BAR (snack form wins) and "Fuel" as a brand word.
    /// </summary>
    [Theory]
    [InlineData("Omega 3 1000mg 120 Soft Gels", "vitamins")]
    [InlineData("Prolab Nutrition Amino Gel-Caps - 200 Softgels", "amino-acids")]
    [InlineData("Salted Orange Electrolyte Drink Mix - 30 Single Sticks", "hydration")]
    [InlineData("Creatine Chews 50-Count", "creatine")]
    [InlineData("SIS Beta Fuel Orange Energy Chewable Bar 45g CHO", "protein-snacks")]
    [InlineData("Fatherhood Fuel Essential Pre-Workout", "pre-workout")]
    [InlineData("PANDA Supplements Fuel Isolate Protein", "protein-powder")]
    // Shopify category text is title + product type; these two went live in
    // the wrong place on the first crawl (2026-09-28) because the dry run only
    // had the stored titles.
    [InlineData("Greens: Cherry Limeade Greens Drink Mix", "vitamins")]
    [InlineData("Core Nutritionals Hydrate Hydration & Sports Drinks", "hydration")]
    public void Look_alikes_stay_where_they_were(string name, string expected)
    {
        Assert.Equal(expected, ProductAttributeParser.InferCategory(name));
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

    /// <summary>
    /// Changed on 2026-09-28: without a gel category this gel stayed in
    /// hydration for its electrolytes. It is a gel first; the electrolytes
    /// are in it, like the caffeine or the carbohydrate in the others.
    /// </summary>
    [Fact]
    public void Gel_with_electrolytes_is_an_energy_gel()
    {
        Assert.Equal("energy-gels-drinks", ProductAttributeParser.InferCategory("Sis Go Energy + Electrolyte Raspberry Gel 60ml"));
    }

    /// <summary>The admin panel accepts only known slugs; the new one must be one.</summary>
    [Fact]
    public void Energy_gels_and_drinks_is_a_valid_category()
    {
        Assert.Contains("energy-gels-drinks", ProductAttributeParser.CategorySlugs);
    }
}
