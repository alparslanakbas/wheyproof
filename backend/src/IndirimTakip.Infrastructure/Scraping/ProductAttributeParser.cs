using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

// Store scrapers pass the product name as is (e.g. "Gold Standard 100% Whey 5 lb
// (Double Rich Chocolate)"). Size, flavor and (when missing) category are parsed
// from that one name, so it's solved in one place for every store instead of
// separate parsing per site. Search working independently of the brand relies on
// this too (see Category).
public static partial class ProductAttributeParser
{
    // Every spelling a store uses for a unit, mapped to one display form.
    // Turkish spellings stay mapped so older shared parsing keeps working.
    private static readonly Dictionary<string, string> UnitCanonical = new(StringComparer.Ordinal)
    {
        ["g"] = "g", ["gr"] = "g", ["gram"] = "g", ["grams"] = "g",
        ["kg"] = "kg", ["kilogram"] = "kg", ["kilograms"] = "kg",
        ["mg"] = "mg",
        ["ml"] = "ml", ["l"] = "L", ["lt"] = "L",
        ["lb"] = "lb", ["lbs"] = "lb", ["pound"] = "lb", ["pounds"] = "lb",
        ["oz"] = "oz", ["ounce"] = "oz", ["ounces"] = "oz",
        ["serving"] = "servings", ["servings"] = "servings",
        ["capsule"] = "capsules", ["capsules"] = "capsules", ["veg capsule"] = "capsules",
        ["veg capsules"] = "capsules", ["vegcapsule"] = "capsules", ["vegcapsules"] = "capsules",
        ["vcaps"] = "capsules", ["caps"] = "capsules",
        ["kapsül"] = "capsules", ["kapsul"] = "capsules", ["kaps"] = "capsules",
        ["tablet"] = "tablets", ["tablets"] = "tablets", ["tab"] = "tablets", ["tabs"] = "tablets",
        ["softgel"] = "softgels", ["softgels"] = "softgels", ["softjel"] = "softgels",
        ["gummy"] = "gummies", ["gummies"] = "gummies",
        ["pack"] = "pack", ["packs"] = "pack", ["pk"] = "pack",
        ["sachet"] = "sachets", ["sachets"] = "sachets", ["şase"] = "sachets", ["sase"] = "sachets",
        ["can"] = "cans", ["cans"] = "cans",
        ["count"] = "count", ["ct"] = "count", ["adet"] = "count",
        ["bar"] = "bars", ["bars"] = "bars",
    };

    private static string CanonicalUnit(string raw)
    {
        var key = WhitespaceRegex().Replace(raw.Trim().ToLowerInvariant(), " ");
        return UnitCanonical.GetValueOrDefault(key, key);
    }

    // Category slugs and the words that identify them, checked in order: the
    // first match wins, so the more specific families come first ("Pre-Workout
    // with Creatine" is a pre-workout; "Mass Gainer Protein" is a gainer).
    // Words match as whole words with an optional plural, so "pump" does not
    // catch "Pumpkin Spice Iced Coffee" and "mass" does not catch "grass".
    // "bulk" is deliberately absent: BulkSupplements would turn every one of
    // its products into a mass gainer.
    private static readonly (string Category, string[] Keywords)[] CategoryKeywords =
    [
        ("pre-workout", ["pre-workout", "pre workout", "preworkout", "pump", "nitric oxide", "stim-free", "caffeine", "glycerol"]),
        ("creatine", ["creatine", "creapure"]),
        ("amino-acids", ["amino", "bcaa", "eaa", "glutamine", "arginine", "citrulline", "beta-alanine", "alanine", "glycine", "taurine", "theanine", "tyrosine", "leucine", "hmb"]),
        ("hydration", ["electrolyte", "hydration", "hydrate"]),
        ("fat-burners", ["fat burner", "burner", "thermogenic", "l-carnitine", "carnitine", "cla", "fat loss", "weight loss"]),
        ("mass-gainers", ["gainer", "mass", "creamy rice", "cream of rice", "carb", "carbohydrate", "maltodextrin", "dextrose", "cyclic dextrin", "highly branched"]),
        ("protein-powder", ["protein", "whey", "isolate", "casein", "collagen"]),
        ("vitamins", ["vitamin", "multivitamin", "mineral", "magnesium", "zinc", "omega", "fish oil", "krill", "biotin", "iron", "calcium", "potassium", "d3", "d3k2", "k2", "b12", "b-complex", "greens", "probiotic", "prebiotic", "synbiotic", "ashwagandha", "turmeric", "curcumin", "ginger", "elderberry", "melatonin", "nmn", "coq10", "berberine", "ginseng", "moringa", "extract", "testosterone", "glucosamine", "chondroitin", "msm", "tudca", "quercetin", "spirulina", "maca", "rhodiola"]),
    ];

    /// <summary>
    /// Every valid category slug: the keyword families plus "protein-snacks",
    /// which is decided by product form rather than a keyword. Built from the
    /// list above so a manual category can never drift from the automatic ones.
    /// </summary>
    public static readonly IReadOnlySet<string> CategorySlugs =
        CategoryKeywords.Select(c => c.Category).Append("protein-snacks").ToHashSet(StringComparer.Ordinal);

    private static readonly (string Category, Regex Pattern)[] CategoryPatterns =
    [
        .. CategoryKeywords.Select(c => (c.Category, new Regex(
            @"(?<![a-z0-9])(?:" + string.Join("|", c.Keywords.Select(Regex.Escape)) + @")(?:s|es)?(?![a-z0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant))),
    ];

    /// <summary>
    /// Package size from a product or variant name, e.g. "5 lb", "250 g",
    /// "30 servings", "240 capsules".
    /// </summary>
    /// <remarks>
    /// A weight wins over a count when both appear ("634 g (186 servings)"):
    /// weight is what the per-gram price needs, and servings are read
    /// separately by <see cref="ExtractServings"/>. "mg" is a dose, not a
    /// package size, so it is used only when nothing else is present
    /// ("Magnesium 400 mg 120 Capsules" is 120 capsules).
    /// </remarks>
    public static string? ExtractSize(string productName)
    {
        var matches = SizeRegex().Matches(productName);
        if (matches.Count == 0)
            return null;

        var candidates = matches
            .Select(m => (Value: m.Groups["value"].Value.Replace(',', '.'), Unit: CanonicalUnit(m.Groups["unit"].Value)))
            .ToList();

        var chosen = candidates.FirstOrDefault(c => WeightUnitsInGrams.ContainsKey(c.Unit));
        if (chosen.Unit is null)
            chosen = candidates.FirstOrDefault(c => c.Unit != "mg");
        if (chosen.Unit is null)
            chosen = candidates[0];

        return $"{chosen.Value} {chosen.Unit}";
    }

    // Grams per unit. 1 lb = 453.59237 g and 1 oz = 28.349523125 g exactly
    // (international avoirdupois definitions). US tubs are sold in pounds, so
    // a mistake here would silently skew every per-gram price.
    private static readonly Dictionary<string, decimal> WeightUnitsInGrams = new(StringComparer.Ordinal)
    {
        ["g"] = 1m,
        ["kg"] = 1000m,
        ["lb"] = 453.59237m,
        ["oz"] = 28.349523125m,
    };

    /// <summary>
    /// Converts a size from <see cref="ExtractSize"/> to grams; null for counts
    /// (servings, capsules), where a weight would be invented.
    /// </summary>
    public static decimal? ToGrams(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return null;

        var parts = size.Trim().Split(' ', 2);
        if (parts.Length != 2
            || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || value <= 0
            || !WeightUnitsInGrams.TryGetValue(CanonicalUnit(parts[1]), out var gramsPerUnit))
        {
            return null;
        }

        return value * gramsPerUnit;
    }

    /// <summary>
    /// Servings when the store states them ("30 Servings", "634 g (186
    /// servings)"). This is the store's own declaration, never a derived guess.
    /// </summary>
    public static int? ExtractServings(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = ServingsCountRegex().Match(text);
        return match.Success
            && int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count is > 0 and <= 2000
                ? count
                : null;
    }

    // Flavor dictionary. A formal rule such as "treat the text in parentheses or
    // after a dash as the flavor" was REJECTED BY REAL DATA: of 80 filled Flavor
    // values on the Turkish site, 42 weren't flavors: servings/amounts ("40
    // Servings", "15 x 4 Doypacks", "1000 IU"), bundle contents ("EAA + HellFire
    // Pre-Workout") and active ingredients ("Arginine", "Collagen", "Maca") had been
    // written into the field. The field is shown as "Flavor: 40 Servings" and is
    // part of SEARCH in DealsQueryService, so the wrong value both showed and
    // matched.
    //
    // So the rule was inverted: a candidate is accepted only when it matches a
    // known flavor word. An unknown new flavor stays empty; a deliberate trade-off
    // in line with "no made-up data": better empty than wrong.
    //
    // The list wasn't invented, it was taken from live data. It holds Turkish and
    // English words because the Turkish stores mixed both; add US flavor words
    // here as they show up.
    private static readonly string[] FlavorWords =
    [
        "çikolata", "chocolate", "çilek", "strawberry", "muz", "banana",
        "vanilya", "vanilla", "karamel", "caramel", "kivi", "ananas",
        "pineapple", "limon", "lemon", "portakal", "orange", "mango",
        "ahududu", "raspberry", "karpuz", "elma", "apple", "şeftali",
        "peach", "böğürtlen", "coconut", "fındık", "hazelnut", "bisküvi",
        "biscuit", "kurabiye", "cookie", "kola", "cheesecake", "tiramisu",
        "kakao", "cocoa", "blueberry", "mandalina", "mandarin", "nar",
        "vişne", "cherry", "dondurma", "frambuaz", "kavun", "kiraz",
        "tropik", "tropical", "aromasiz", "naturel", "sade", "bal",
        "tarçın", "fistik", "badem", "latte", "kahve", "coffee", "mocha",
        "nane", "mint", "meyve", "krema", "cream", "yogurt", "yoğurt",
    ];

    // Multi-word flavors are separate: they are searched directly, not by token
    // match (coconut, blueberry, forest fruit in Turkish).
    private static readonly string[] FlavorPhrases =
    [
        "hindistan cevizi", "yaban mersini", "orman meyve",
    ];

    /// <summary>
    /// Turkish consonant softening: a suffixed word's final consonant changes
    /// (çilek → çileği, "strawberry"). Looking only for words starting with "çilek"
    /// would have missed "Ereğli Çileği", a real product. So the softened stem of
    /// every flavor word is matched too.
    /// </summary>
    private static string SoftenFinalConsonant(string word) => word.Length == 0 ? word : word[^1] switch
    {
        'k' => string.Concat(word.AsSpan(0, word.Length - 1), "ğ"),
        'p' => string.Concat(word.AsSpan(0, word.Length - 1), "b"),
        't' => string.Concat(word.AsSpan(0, word.Length - 1), "d"),
        'ç' => string.Concat(word.AsSpan(0, word.Length - 1), "c"),
        _ => word,
    };

    // Stems used for matching: the dictionary itself + its softened forms.
    private static readonly string[] FlavorStems =
        [.. FlavorWords.Concat(FlavorWords.Select(SoftenFinalConsonant)).Distinct()];

    /// <summary>
    /// Normalization for the Turkish letter trap. "AROMASIZ" becomes "aromasiz" with
    /// ToLowerInvariant while a dictionary word may carry the dotless ı; "ÇİLEK"
    /// isn't lowercased at all by the invariant culture because of the dotted İ. The
    /// dotted/dotless distinction is removed on both sides.
    /// </summary>
    private static string NormalizeForFlavorMatch(string value) =>
        value.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

    public static string? ExtractFlavor(string productName)
    {
        foreach (Match match in ParenthesesRegex().Matches(productName))
        {
            var content = match.Groups[1].Value.Trim();
            if (LooksLikeFlavor(content))
                return content;
        }

        // Second source: a " - Flavor" suffix. Some store APIs return every flavor
        // as a separate product and append the flavor to the name ("BCAA 4:1:1 -
        // Pineapple") without using parentheses.
        var lastDash = productName.LastIndexOf(" - ", StringComparison.Ordinal);
        if (lastDash >= 0)
        {
            var tail = productName[(lastDash + 3)..].Trim();
            if (LooksLikeFlavor(tail))
                return tail;
        }

        return null;
    }

    private static bool LooksLikeFlavor(string candidate)
    {
        if (candidate.Length == 0)
            return false;

        // A candidate containing digits is almost always an amount/serving.
        // Live data had not a single real flavor with a digit.
        if (candidate.Any(char.IsDigit))
            return false;

        // "+" marks bundle contents, "®"/"™" a brand name.
        if (candidate.Contains('+') || candidate.Contains('®') || candidate.Contains('™'))
            return false;

        if (SizeRegex().IsMatch(candidate))
            return false;

        var normalized = NormalizeForFlavorMatch(candidate);

        if (FlavorPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal)))
            return true;

        // Word-based matching: "which word does it START with" instead of "does it
        // contain". Turkish suffixes are caught ("çilekli", "muzlu", "limonlu"),
        // while short words ("bal", "nar": honey, pomegranate) don't match wrongly
        // in the middle of another word.
        var tokens = normalized.Split([' ', '-', '/', ',', '.', '&', '(', ')', '*'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => FlavorStems.Any(stem => token.StartsWith(stem, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Removes occurrences of the brand name from the product name. Without a brand
    /// the name is returned as is.
    ///
    /// Note: the comparison is OrdinalIgnoreCase, which works for ASCII brand names.
    /// It may not match brand names with Turkish letters (dotted/dotless i); no such
    /// case has been seen, add normalization if it comes up.
    /// </summary>
    private static string StripBrandName(string productName, string? brandName)
    {
        if (string.IsNullOrWhiteSpace(brandName))
            return productName;

        var stripped = productName.Replace(brandName, " ", StringComparison.OrdinalIgnoreCase);

        // A brand name can be written without spaces too ("Proteinocean" / "Protein Ocean").
        var compact = brandName.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length > 3 && compact.Length != brandName.Length)
            stripped = stripped.Replace(compact, " ", StringComparison.OrdinalIgnoreCase);

        // If the whole name is the brand, nothing is left to infer from; returning
        // the original beats a wrong category.
        return stripped.Trim().Length == 0 ? productName : stripped;
    }

    /// <summary>
    /// Infers the category from a product name.
    /// </summary>
    /// <param name="brandName">
    /// The manufacturer brand if known; it is REMOVED from the name. Retailer
    /// sources write the brand into the product name ("Proteinocean Creatine 300gr")
    /// and when the brand name contains a category keyword the product lands in the
    /// wrong category: in real data one brand's creatine, omega and vitamins were
    /// stored as protein powder, because "protein" matched inside the brand name.
    ///
    /// The category must come from WHAT the product is, not who makes it.
    /// </param>
    public static string? InferCategory(string productName, string? brandName = null)
    {
        productName = StripBrandName(productName, brandName);

        // ToLowerInvariant on purpose: in the tr-TR culture an uppercase "I" becomes
        // a dotless "ı" ("CREATINE" -> "creatıne"), which never matched English
        // keywords such as "creatine". There's a second, separate trap: the Turkish
        // uppercase dotted "İ" (e.g. "C VİTAMİNİ") is NOT lowercased at all by
        // ToLowerInvariant (the invariant culture has no simple mapping for it), so
        // "vİtamİnİ" never matched the "vitamin" keyword (one of the real reasons
        // hundreds of products stayed uncategorised). It's fixed by hand with
        // .Replace, without going back to a culture-sensitive ToLower.
        var normalized = productName.Replace('İ', 'i').ToLowerInvariant();

        // THE PRODUCT'S FORM COMES BEFORE ITS CONTENT. The keyword list is checked
        // in order with protein powder near the top, so a BAR with "protein" in its
        // name landed in the powder category. Measured on the Turkish site: 39
        // protein bars across 11 brands were tagged as powder while 15 were in the
        // right category, so one product type was split across two categories.
        //
        // The word boundary is REQUIRED: the list searched the substring "bar", so a
        // barbecue seasoning counted as a snack.
        // Snack words are also flavor names: "Cookies & Cream Protein Powder",
        // "Protein Powder: Brownie Batter", "GHOST WHEY | Cocoa Puffs". In the
        // first US crawl 38 of 329 snacks were powders, stacks or sample
        // packets, so a powder or serving word vetoes the snack rule.
        if (SnackBarFormRegex().IsMatch(normalized) && !PowderFormRegex().IsMatch(normalized))
            return "protein-snacks";

        foreach (var (category, pattern) in CategoryPatterns)
        {
            if (pattern.IsMatch(normalized))
                return category;
        }

        return null;
    }

    // Synonym groups for the search box, a structure DELIBERATELY SEPARATE from
    // CategoryKeywords. The CategoryKeywords lists are right for CATEGORY DETECTION
    // (deciding which category a product belongs to needs a broad, mixed pool of
    // words; the vitamins category holding 35+ unrelated ingredients is no problem
    // there). But using those broad lists as SEARCH SYNONYMS (treating the whole
    // category as synonyms when a shopper types one word) gave wrong results: a
    // magnesium search returned the entire vitamins category (NMN, ZMA, biotin, none
    // related to magnesium) as synonyms and put it on top. Checking the same pattern
    // in EVERY category showed amino acids and mass gainers were broken the same way
    // (taurine, glutamine and arginine searches ALL returned the same 83 products in
    // the same order).
    //
    // The fix: CategoryKeywords stays AS IS for each category (category detection is
    // unaffected), and ONLY narrow sub-groups that really are different spellings,
    // languages or brand names of the same concept are defined here. Different
    // ingredients in the same category (e.g. glycine and taurine, both amino acids
    // but not synonyms of each other) DELIBERATELY belong to no group; they are
    // searched on their own.
    private static readonly string[][] SynonymGroups =
    [
        ["pre workout", "preworkout", "pre-workout"],
        ["creatine", "creapure"],
        ["bcaa", "branched chain amino acids"],
        ["eaa", "essential amino acids"],
        ["electrolyte", "electrolytes", "hydration"],
        ["gainer", "mass gainer"],
        ["burner", "fat burner", "thermogenic"],
        ["multivitamin", "multi vitamin"],
        ["isolate", "whey isolate"],
    ];

    public static IReadOnlyCollection<string> GetSearchSynonyms(string term)
    {
        foreach (var group in SynonymGroups)
        {
            if (group.Contains(term, StringComparer.Ordinal))
                return group;
        }

        return [];
    }

    // Extracts the serving size from the store's own product description. Some
    // stores provide it structurally (a nutrition table), others only in free text.
    // The four patterns below match the Turkish wordings seen in descriptions ("1
    // ölçek (30 g)" = 1 scoop, "Porsiyon Büyüklüğü: 25 g" = serving size, "Servis
    // başına 23 g" = per serving); none of them match English text, so US stores
    // need the structured value. If none matches it returns null (NO guessing: an
    // assumption like "30 g = 1 serving" was deliberately never made).
    public static decimal? ExtractServingSizeGrams(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        foreach (var regex in ServingSizeRegexes)
        {
            var match = regex().Match(description);
            if (!match.Success)
                continue;

            if (!decimal.TryParse(
                    match.Groups["value"].Value.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var grams))
            {
                continue;
            }

            // Drop unreasonable matches: in real data the smallest servings were 1 g
            // for single amino acids (citrulline/glycine) and the largest around
            // 200 g for gainers. A number outside that range was captured from a
            // part of the text unrelated to servings (e.g. 0.22 g in a sauce).
            if (grams is >= 1m and <= 500m)
                return grams;
        }

        return null;
    }

    // Order matters: from the most explicit, least error-prone pattern ("serving
    // size: 30 g") to the loosest one last.
    private static readonly Func<Regex>[] ServingSizeRegexes =
    [
        ServingPortionRegex,
        ServingScoopParenRegex,
        ServingScoopReversedRegex,
        ServingServisRegex,
    ];

    [GeneratedRegex(@"porsiyon[^0-9]{0,25}(?<value>\d+(?:[.,]\d+)?)\s*(?:gr|gram|g)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServingPortionRegex();

    [GeneratedRegex(@"ölçek[^0-9]{0,15}(?<value>\d+(?:[.,]\d+)?)\s*(?:gr|gram|g)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServingScoopParenRegex();

    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?:gr|gram|g)\b[^a-zçğışöü]{0,10}ölçek", RegexOptions.IgnoreCase)]
    private static partial Regex ServingScoopReversedRegex();

    [GeneratedRegex(@"servis[^0-9]{0,20}(?<value>\d+(?:[.,]\d+)?)\s*(?:gr|gram|g)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServingServisRegex();

    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>kilograms?|kg|grams?|gr|g|mg|ml|lbs?|pounds?|ounces?|oz|lt|l|servings?|veg\s*capsules?|capsules?|vcaps|caps|kaps[uü]l|kaps|softgels?|softjel|tablets?|tabs?|gummies|gummy|packs?|pk|sachets?|cans?|count|ct|bars?|adet|[şs]ase)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex ParenthesesRegex();

    /// <summary>
    /// Words saying the product is a bar or snack, with word boundaries so they
    /// don't hit inside words such as "Barbell".
    /// </summary>
    // "protein bites" and "protein candy" as phrases: Bounce's snacks were filed
    // as protein powder (2026-09-14). The words alone can't be added: "Cotton
    // Candy" is a pre-workout flavor and "Brownie Bites" a powder flavor.
    [GeneratedRegex(@"\b(bars?|cookies?|chips|crisps|puffs|brownies?|wafers?|pretzels?|protein\s+(bites|candy))\b", RegexOptions.IgnoreCase)]
    private static partial Regex SnackBarFormRegex();

    /// <summary>
    /// Words that say the product is a powder or a dosed supplement, not a
    /// snack, whatever its flavor is called.
    /// </summary>
    [GeneratedRegex(@"\b(powders?|whey|isolate|casein|servings?|packets?|scoops?|canisters?|tubs?|stacks?|shakes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PowderFormRegex();

    [GeneratedRegex(@"(?<count>\d+)\s*servings?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServingsCountRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
