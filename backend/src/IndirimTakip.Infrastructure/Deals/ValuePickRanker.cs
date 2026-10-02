using System.Text.RegularExpressions;
using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Deals;

/// <summary>
/// A candidate row for the product list on the "Which supplement?" pages, as
/// it comes back from the query.
/// </summary>
public sealed record ValuePickCandidate(
    int ProductId,
    int BrandId,
    string BrandName,
    string ProductName,
    string? Size,
    decimal Price,
    bool? InStock);

public sealed record RankedValuePick(ValuePickCandidate Candidate, decimal PricePerKg);

/// <summary>The chosen products and how many products had a computable price per kg.</summary>
public sealed record ValuePickRanking(IReadOnlyList<RankedValuePick> Picks, int EligibleCount);

/// <summary>
/// Ranks the products of a category by PRICE PER KILOGRAM and takes one
/// product per brand. (The US site shows it per pound; that is a display
/// conversion in the frontend.)
///
/// WHY WEIGHT: protein per serving is known for only a minority of products,
/// so a "cheapest per gram of protein" ranking would leave most of the catalog
/// out and make a false "cheapest" claim. Package weight is known far more
/// often. Products sold by count ("30 servings") have no weight and are left
/// out, never guessed.
///
/// WHY THE GUARDS: a raw price-per-kg ranking puts the wrong products on top
/// (measured on the ProteinAvcisi catalog, the same code base, 2026-09-29):
/// cream of rice and protein pancake mix at the head of the protein list,
/// gainer + creatine bundles in the creatine list, a "3000 Kg" typo at 0 per
/// kg, and out-of-stock products. The category label alone isn't enough, so
/// the product's form is checked too. The rules were dry-run against the live
/// US and UK candidates.
///
/// ONE PRODUCT PER BRAND: the same tub in several sizes and flavors would
/// otherwise fill the list. Each brand's lowest price per kg is taken; the
/// page states the rule.
/// </summary>
public static partial class ValuePickRanker
{
    public const int DefaultCount = 6;
    public const int MaxCount = 12;

    private sealed record CategoryRule(decimal MinGrams, decimal MaxGrams, Regex? OtherProduct);

    // The floor drops single-serving sachets and samples, the ceiling unit
    // errors. Small trial sizes are expensive per kg anyway; the floor exists
    // for the cheap-looking wrong products.
    private static readonly Dictionary<string, CategoryRule> Rules = new(StringComparer.Ordinal)
    {
        ["protein-powder"] = new(400m, 6000m, NotProteinPowderRegex()),
        ["creatine"] = new(100m, 2000m, CreatineBlendRegex()),
        ["mass-gainers"] = new(1000m, 10000m, null),
        ["energy-gels-drinks"] = new(200m, 5000m, null),
        // "Clear Whey Hydrate" is a protein drink filed under hydration (UK dry run).
        ["hydration"] = new(100m, 5000m, NotElectrolyteRegex()),
    };

    private static readonly CategoryRule DefaultRule = new(100m, 10000m, null);

    /// <summary>
    /// Narrowing types within a category. Protein powder follows the quiz's
    /// dairy question (isolate / plant); mass gainers and plain carbohydrate
    /// powders are listed apart, because per kg the carbohydrate powders take
    /// every top spot of a shared list.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, Func<string, bool>>> Types = new(StringComparer.Ordinal)
    {
        ["protein-powder"] = new(StringComparer.Ordinal)
        {
            // Isolate and hydrolyzed whey are very low in lactose; an
            // "isolate & concentrate" blend still carries concentrate.
            ["isolate"] = name => IsolateRegex().IsMatch(name) && !NotLowLactoseRegex().IsMatch(name),
            ["plant"] = name => PlantRegex().IsMatch(name),
        },
        ["mass-gainers"] = new(StringComparer.Ordinal)
        {
            ["gainer"] = IsGainer,
            ["carbs"] = name => !IsGainer(name),
        },
    };

    /// <summary>Is the type defined for this category? (The endpoint asks, to keep free text out of the cache.)</summary>
    public static bool IsValidType(string category, string type) =>
        Types.TryGetValue(category, out var types) && types.ContainsKey(type);

    public static ValuePickRanking Rank(
        IEnumerable<ValuePickCandidate> candidates, string category, string? type, int count)
    {
        var rule = Rules.GetValueOrDefault(category, DefaultRule);
        Func<string, bool> matchesType = type is null
            ? _ => true
            : Types.GetValueOrDefault(category)?.GetValueOrDefault(type) ?? (_ => false);

        var eligible = candidates
            // A store that doesn't report stock (null) stays: calling the
            // unknown "out of stock" would be made up too.
            .Where(c => c.InStock != false && c.Price > 0)
            .Select(c => (Candidate: c, Grams: ProductAttributeParser.ToGrams(c.Size)))
            .Where(x => x.Grams is decimal grams && grams >= rule.MinGrams && grams <= rule.MaxGrams)
            .Select(x => (x.Candidate, x.Grams, Name: DealsQueryService.NormalizeSearchText(x.Candidate.ProductName)))
            .Where(x => !BundleRegex().IsMatch(x.Name))
            .Where(x => rule.OtherProduct is null || !rule.OtherProduct.IsMatch(x.Name))
            .Where(x => matchesType(x.Name))
            .Select(x => new RankedValuePick(x.Candidate, Math.Round(x.Candidate.Price / x.Grams!.Value * 1000m, 2)))
            // Id breaks ties, so products with the same price per kg keep one
            // order across requests (the cache and SSR show the same list).
            .OrderBy(x => x.PricePerKg)
            .ThenBy(x => x.Candidate.ProductId)
            .ToList();

        var picks = eligible
            .DistinctBy(x => x.Candidate.BrandId)
            .Take(Math.Clamp(count, 1, MaxCount))
            .ToList();

        return new ValuePickRanking(picks, eligible.Count);
    }

    private static bool IsGainer(string name) => GainerRegex().IsMatch(name) && !PlainCarbRegex().IsMatch(name);

    // Multi-product sets: "... Bundle", "... Stack", "Protein + Creatine".
    // (BundleProductFilter looks for the Turkish words it was written for.)
    [GeneratedRegex(@"\b(bundle|stack|kit|combo)\b| \+ ")]
    private static partial Regex BundleRegex();

    // Other products filed under protein powder. FLAVOR NAMES ARE LEFT OUT ON
    // PURPOSE: "Cookies & Cream", "Birthday Cake", "Chocolate Peanut Butter"
    // are protein flavors, and matching them would drop real powders; hence
    // "powdered peanut butter" in full. Snacks in the same form sit under the
    // 400 g floor anyway. Collagen isn't a complete protein and has no place in
    // a list recommended for muscle. From the US/UK dry run: "Clear Protein
    // Water (16 Oz. | 4 Pack)" is bottled (a "2+ pack" means ready-to-drink
    // bottles, while "1 Pack - 582g" is a powder), and "Vegan Gainz" is a gainer.
    [GeneratedRegex(@"creatine|cream of rice|creamy rice|gainer|gainz|\bmass\b|collagen|pancake|waffle|pudding|\bmeal\b|recovery|pre-?workout|carbohydrate|\bcarbs?\b|powdered peanut butter|peanut butter powder|\bwater\b|\b([2-9]|\d{2,})\s*pack\b")]
    private static partial Regex NotProteinPowderRegex();

    [GeneratedRegex(@"\bwhey\b|\bprotein\b|creatine")]
    private static partial Regex NotElectrolyteRegex();

    // For a creatine monohydrate recommendation, blends are out: gainer or
    // protein + creatine, creatine + carbs, creatine with aminos or
    // electrolytes.
    [GeneratedRegex(@"gainer|\bmass\b|\bwhey\b|\bprotein\b|carb|glutamine|taurine|bcaa|\beaa\b|amino|arginine|pre-?workout|\bpump\b|electrolyte|hydration")]
    private static partial Regex CreatineBlendRegex();

    [GeneratedRegex(@"isolate|\biso\d*\b|isopure|hydroly[sz]")]
    private static partial Regex IsolateRegex();

    // A blend still carries concentrate; casein hydrolysate isn't whey.
    [GeneratedRegex(@"concentrate|blend|casein")]
    private static partial Regex NotLowLactoseRegex();

    [GeneratedRegex(@"vegan|plant|\bpea\b|\bsoy\b|rice protein|hemp|pumpkin|fava")]
    private static partial Regex PlantRegex();

    [GeneratedRegex(@"gainer|gainz|\bmass\b|\bgain\b")]
    private static partial Regex GainerRegex();

    // A product named for a single carbohydrate source isn't a gainer.
    [GeneratedRegex(@"maltodextrin|dextrose|cream of rice|creamy rice|cyclic dextrin|highly branched|cluster dextrin|vitargo")]
    private static partial Regex PlainCarbRegex();
}
