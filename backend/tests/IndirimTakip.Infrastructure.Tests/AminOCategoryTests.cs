using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// Optimum Nutrition spells its amino line "AMIN.O.": the dots defeated the
// "amino" keyword and seven powders had no category (2026-10-05). Names are
// the live ones from both markets.
public class AminOCategoryTests
{
    [Theory]
    [InlineData("ESSENTIAL AMIN.O. ENERGY® - 30 Servings (0.6 lb)")]
    [InlineData("2x ESSENTIAL AMIN.O. ENERGY® (30 serv)")]
    [InlineData("ESSENTIAL AMIN.O. ENERGY® Sample Packets - 1 Packet")]
    [InlineData("Essential AMIN.O. Energy Powder - 270g (30 Servings) - 270 g (30 servings)")]
    [InlineData("Essential AMIN.O. Energy Powder - Elite Series - Fruit Fusion - 270g (30 Servings)")]
    public void The_powders_are_amino_acids(string name) =>
        Assert.Equal("amino-acids", ProductAttributeParser.InferCategory(name, "Optimum Nutrition"));

    // Sold as hydration products and already filed there; the AMIN.O. rule
    // comes after hydration so they don't move.
    [Theory]
    [InlineData("ESSENTIAL AMIN.O. ENERGY+ Electrolytes Sparkling - 12 Cans")]
    [InlineData("Essential AMIN.O. ENERGY + Electrolytes Stick Packs - 7 Stick Packs")]
    [InlineData("AMIN.O. ENERGY HYDRATION + FOCUS - 30 Servings (0.6 lb)")]
    public void The_hydration_drinks_stay_in_hydration(string name) =>
        Assert.Equal("hydration", ProductAttributeParser.InferCategory(name, "Optimum Nutrition"));
}
