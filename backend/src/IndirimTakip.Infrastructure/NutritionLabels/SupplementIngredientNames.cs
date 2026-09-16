using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Ingredient names a Supplement Facts row may carry when read by OCR.
/// </summary>
/// <remarks>
/// <b>This list is the typo guard.</b> Tesseract reads the amounts well but
/// garbles names: "Iron" as "lron", "Vitamin B6" as "Vitamin Be",
/// "L-Isoleucine" as "l-ilsoleucine" (measured on 26 labels, 2026-09-17). A
/// row whose name matches nothing here rejects the whole label instead of
/// publishing the misspelling.
///
/// Matched as whole words anywhere in the name, so branded rows pass on their
/// generic part ("CarnoSyn Beta-Alanine", "Purcaf Organic Caffeine"). Vitamins
/// are listed with their letter: "vitamin" alone would let "Vitamin Be" through.
/// "blend", "complex" and "matrix" admit proprietary blend totals, which print
/// no ingredient name to check.
/// </remarks>
internal static partial class SupplementIngredientNames
{
    private static readonly string[] Terms =
    [
        // Vitamins
        "vitamin a", "vitamin b1", "vitamin b2", "vitamin b3", "vitamin b5", "vitamin b6", "vitamin b7", "vitamin b9",
        "vitamin b12", "vitamin c", "vitamin d", "vitamin d2", "vitamin d3", "vitamin e", "vitamin k", "vitamin k1",
        "vitamin k2", "thiamin", "thiamine", "riboflavin", "niacin", "niacinamide", "folate", "folic acid", "biotin",
        "pantothenic acid", "choline", "inositol", "beta carotene",
        // Minerals and electrolytes
        "calcium", "iron", "magnesium", "zinc", "sodium", "potassium", "phosphorus", "iodine", "selenium", "copper",
        "manganese", "chromium", "molybdenum", "chloride", "boron", "electrolytes", "salt", "bicarbonate",
        // Sugars and carbohydrate sources
        "sugars", "added sugars", "sugar alcohol", "erythritol", "allulose", "palatinose", "isomaltulose", "dextrose",
        "maltodextrin", "cluster dextrin",
        // Amino acids and related
        "leucine", "isoleucine", "valine", "bcaa", "bcaas", "eaa", "eaas", "amino acid", "amino acids", "glutamine",
        "lysine", "threonine", "methionine", "phenylalanine", "tryptophan", "histidine", "arginine", "citrulline",
        "ornithine", "taurine", "tyrosine", "theanine", "glycine", "alanine", "carnosine", "carnitine", "hmb",
        "hydroxymethylbutyrate", "glutathione", "cysteine", "proline", "serine", "aspartic acid", "glutamic acid",
        "agmatine", "betaine", "trimethylglycine",
        // Performance
        "creatine", "glycerol", "glycerin", "nitrate", "beet root", "beetroot", "caffeine", "theobromine", "theacrine",
        "dynamine", "paraxanthine", "yohimbine", "yohimbe", "synephrine", "huperzine", "alpha gpc", "citicoline",
        "tyrosol", "bioperine", "black pepper", "piperine", "ketones", "beta hydroxybutyrate", "bhb",
        // Fats and oils
        "cla", "conjugated linoleic acid", "omega 3", "omega 3s", "epa", "dha", "fish oil", "krill oil", "phospholipids",
        "mct", "mct oil",
        // Other common rows
        "astaxanthin", "collagen", "hyaluronic acid", "probiotics", "probiotic", "prebiotic", "bacillus", "lactobacillus",
        "bifidobacterium", "inulin", "ashwagandha", "rhodiola", "ginseng", "astragalus", "ginger", "turmeric",
        "curcumin", "green tea", "egcg", "epigallocatechin", "garcinia", "coq10", "coenzyme q10", "ubiquinol",
        "alpha lipoic acid", "resveratrol", "quercetin", "melatonin", "valerian", "gaba", "mucuna", "tongkat ali",
        "fenugreek", "tribulus", "saw palmetto", "zma", "baobab", "coconut water", "spirulina", "chlorella",
        "wheatgrass", "greens", "superfoods", "mushroom", "cordyceps", "reishi", "chaga", "lion s mane", "elderberry",
        "echinacea", "enzymes", "protease", "amylase", "lipase", "lactase", "bromelain", "papain", "msm", "glucosamine",
        "chondroitin", "tudca", "nmn", "berberine", "apple cider vinegar", "psyllium", "moringa", "maca", "cacao",
        "cocoa", "matcha", "coffee", "guarana", "yerba mate", "peptides",
        // Proprietary blend totals
        "blend", "complex", "matrix", "extract",
    ];

    private static readonly Regex KnownName = new(
        @"\b(?:" + string.Join("|", Terms.Select(t => Regex.Escape(t).Replace(@"\ ", @"\s+"))) + @")\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Whether a cleaned row name contains a known ingredient.</summary>
    public static bool IsKnown(string name) => KnownName.IsMatch(Fold(name));

    /// <summary>Lower case, marks gone, hyphens and apostrophes as spaces ("L-Leucine" -> "l leucine").</summary>
    public static string Fold(string name)
    {
        var lowered = name.ToLowerInvariant().Replace('®', ' ').Replace('™', ' ').Replace('©', ' ');
        var spaced = SeparatorRegex().Replace(lowered, " ").Trim();
        // "Vitamin B-6" -> "vitamin b 6" -> "vitamin b6".
        return VitaminNumberRegex().Replace(spaced, "$1$2");
    }

    [GeneratedRegex(@"[\s\-_'’]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\b([bdk]) (\d{1,2})\b")]
    private static partial Regex VitaminNumberRegex();
}
