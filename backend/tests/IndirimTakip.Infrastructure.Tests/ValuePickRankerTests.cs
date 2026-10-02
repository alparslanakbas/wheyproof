using IndirimTakip.Infrastructure.Deals;

namespace IndirimTakip.Infrastructure.Tests;

// Product names are from the live US and UK catalogs (2026-09-30 dry run).
// Each guard answers a real wrong product that a raw price-per-kg ranking put
// on top.
public class ValuePickRankerTests
{
    private static int _lastId;

    private static ValuePickCandidate Candidate(string name, string? size, decimal price, int brandId = 0, bool? inStock = true) =>
        new(++_lastId, brandId == 0 ? _lastId + 1000 : brandId, $"Brand {brandId}", name, size, price, inStock);

    private static List<string> Names(ValuePickRanking ranking) =>
        ranking.Picks.Select(p => p.Candidate.ProductName).ToList();

    [Theory]
    [InlineData("Powdered Peanut Butter | Naked PB - 2LB")]
    [InlineData("Clear Protein Water: Blackberry Vanilla (16 Oz. | 4 Pack)")]
    [InlineData("The Bulk Protein Company Vegan Gainz - 4kg")]
    [InlineData("Creatine Monohydrate Powder - 1kg")]
    [InlineData("Cream Of Rice Apple Strudel - 2Kg")]
    [InlineData("Collagen Peptides - 1 lb")]
    [InlineData("Protein Pancake Mix - 2 lb")]
    [InlineData("Whey Protein + Creatine Bundle - 5 lb")]
    public void ExcludesProductsThatAreNotProteinPowder(string name)
    {
        var ranking = ValuePickRanker.Rank([Candidate(name, "2 lb", 30m)], "protein-powder", null, 6);

        Assert.Empty(ranking.Picks);
    }

    // Flavor names must not be taken for a product form.
    [Theory]
    [InlineData("Chocolate Peanut Butter Pea Protein Powder | Naked Pea - 5LB")]
    [InlineData("Gold Standard 100% Whey - Cookies & Cream - 5 lb")]
    [InlineData("Birthday Cake Whey Isolate - 2 lb")]
    [InlineData("Premium Protein Powder - 1 Pack - 582g")]
    public void KeepsFlavorsAndSinglePacks(string name)
    {
        var ranking = ValuePickRanker.Rank([Candidate(name, "2 lb", 30m)], "protein-powder", null, 6);

        Assert.Single(ranking.Picks);
    }

    [Fact]
    public void DropsOutOfStockKeepsUnknownStock()
    {
        var candidates = new[]
        {
            Candidate("Whey A - 2 lb", "2 lb", 20m, inStock: false),
            Candidate("Whey B - 2 lb", "2 lb", 25m, inStock: null),
            Candidate("Whey C - 2 lb", "2 lb", 30m, inStock: true),
        };

        var ranking = ValuePickRanker.Rank(candidates, "protein-powder", null, 6);

        Assert.Equal(["Whey B - 2 lb", "Whey C - 2 lb"], Names(ranking));
    }

    [Fact]
    public void RanksByPricePerKgWithOneProductPerBrand()
    {
        var candidates = new[]
        {
            // Brand 1: the 5 lb tub is cheaper per kg and wins.
            Candidate("Whey 2 lb", "2 lb", 40m, brandId: 1),
            Candidate("Whey 5 lb", "5 lb", 80m, brandId: 1),
            Candidate("Isolate 1 kg", "1 kg", 45m, brandId: 2),
            Candidate("Whey 2 kg", "2 kg", 60m, brandId: 3),
        };

        var ranking = ValuePickRanker.Rank(candidates, "protein-powder", null, 6);

        Assert.Equal(["Whey 2 kg", "Whey 5 lb", "Isolate 1 kg"], Names(ranking));
        // 80 / 2.26796 kg = 35.27 per kg.
        Assert.Equal([30m, 35.27m, 45m], ranking.Picks.Select(p => p.PricePerKg));
        Assert.Equal(4, ranking.EligibleCount);
    }

    [Fact]
    public void LeavesOutProductsSoldByCount()
    {
        var candidates = new[]
        {
            Candidate("Creatine - 90 servings", "90 servings", 30m),
            Candidate("Creatine Gummies", "60 gummies", 25m),
            Candidate("Creatine Monohydrate - 500 g", "500 g", 25m),
        };

        var ranking = ValuePickRanker.Rank(candidates, "creatine", null, 6);

        Assert.Equal(["Creatine Monohydrate - 500 g"], Names(ranking));
    }

    [Fact]
    public void ExcludesCreatineBlends()
    {
        var candidates = new[]
        {
            Candidate("Creatine + Electrolytes - 300 g", "300 g", 20m),
            Candidate("Creatine Hydration - 300 g", "300 g", 20m),
            Candidate("Mass Gainer with Creatine - 5 lb", "5 lb", 50m),
            Candidate("Creatine Monohydrate (Micronized) Powder - 1 Kilogram", "1 kg", 30m),
        };

        var ranking = ValuePickRanker.Rank(candidates, "creatine", null, 6);

        Assert.Equal(["Creatine Monohydrate (Micronized) Powder - 1 Kilogram"], Names(ranking));
    }

    [Fact]
    public void IsolateTypeKeepsLowLactoseOnly()
    {
        var candidates = new[]
        {
            Candidate("Gold Standard 100% Isolate Whey Protein powder - 3 lb", "3 lb", 100m),
            Candidate("Platinum Hydrowhey Hydrolysed Whey Protein Powder - 1.6kg", "1.6 kg", 108m),
            Candidate("Whey Protein Blend (Isolate & Concentrate) - 5 lb", "5 lb", 70m),
            Candidate("Pure Casein Hydrolysate - 1kg", "1 kg", 110m),
            Candidate("100% Whey Protein - 5 lb", "5 lb", 60m),
        };

        var names = Names(ValuePickRanker.Rank(candidates, "protein-powder", "isolate", 6));

        Assert.Equal(2, names.Count);
        Assert.DoesNotContain("Whey Protein Blend (Isolate & Concentrate) - 5 lb", names);
        Assert.DoesNotContain("Pure Casein Hydrolysate - 1kg", names);
    }

    [Fact]
    public void PlantTypeKeepsPlantProteins()
    {
        var candidates = new[]
        {
            Candidate("Nutricost Organic Rice Protein Powder - 5 LBS", "5 lb", 55m),
            Candidate("Performance Protein - Vegan Protein Powder - 520g", "520 g", 39m),
            Candidate("100% Whey Protein - 5 lb", "5 lb", 60m),
        };

        var names = Names(ValuePickRanker.Rank(candidates, "protein-powder", "plant", 6));

        Assert.Equal(2, names.Count);
        Assert.DoesNotContain("100% Whey Protein - 5 lb", names);
    }

    // In one list the carbohydrate powders take every top spot per kg; gainers
    // are listed apart. "Bulk" is a UK brand, not a gainer word.
    [Fact]
    public void SeparatesGainersFromCarbPowders()
    {
        var candidates = new[]
        {
            Candidate("Maltodextrin - 5kg", "5 kg", 17m),
            Candidate("Cream Of Rice Apple Strudel - 2Kg", "2 kg", 30m),
            Candidate("Bulk 1000 Mass Gainer - 4.05kg", "4.05 kg", 40m),
            Candidate("Serious Mass - 16 Servings (12 lb)", "12 lb", 115m),
        };

        var gainers = Names(ValuePickRanker.Rank(candidates, "mass-gainers", "gainer", 6));
        var carbs = Names(ValuePickRanker.Rank(candidates, "mass-gainers", "carbs", 6));

        Assert.Equal(["Bulk 1000 Mass Gainer - 4.05kg", "Serious Mass - 16 Servings (12 lb)"], gainers);
        Assert.Equal(["Maltodextrin - 5kg", "Cream Of Rice Apple Strudel - 2Kg"], carbs);
    }

    [Fact]
    public void KeepsProteinDrinksOutOfHydration()
    {
        var candidates = new[]
        {
            Candidate("Clear Whey Hydrate Blueberry Peach - 900g", "900 g", 45m),
            Candidate("Electrolyte Powder - 264g (33 Servings)", "264 g", 10m),
        };

        var ranking = ValuePickRanker.Rank(candidates, "hydration", null, 6);

        Assert.Equal(["Electrolyte Powder - 264g (33 Servings)"], Names(ranking));
    }

    [Theory]
    [InlineData("protein-powder", "isolate", true)]
    [InlineData("protein-powder", "plant", true)]
    [InlineData("mass-gainers", "gainer", true)]
    [InlineData("mass-gainers", "carbs", true)]
    [InlineData("creatine", "isolate", false)]
    [InlineData("protein-powder", "gainer", false)]
    [InlineData("protein-powder", "ISOLATE", false)]
    public void TypeIsValidatedPerCategory(string category, string type, bool expected)
    {
        Assert.Equal(expected, ValuePickRanker.IsValidType(category, type));
    }

    [Fact]
    public void CountIsClamped()
    {
        var candidates = Enumerable.Range(1, 20).Select(i => Candidate($"Whey {i}", "2 lb", 30m + i)).ToList();

        Assert.Equal(3, ValuePickRanker.Rank(candidates, "protein-powder", null, 3).Picks.Count);
        Assert.Equal(ValuePickRanker.MaxCount, ValuePickRanker.Rank(candidates, "protein-powder", null, 99).Picks.Count);
    }
}
