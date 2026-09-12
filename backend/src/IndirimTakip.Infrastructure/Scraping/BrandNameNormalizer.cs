namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Maps manufacturer names to one canonical spelling.
///
/// WHY: retailers spell the same manufacturer differently. With two spellings as
/// two `Brand` rows the brand was split in two, and that wasn't only cosmetic:
///
///   • Both resolve to the same URL (the brand slug is lowercased), so the sitemap
///     got DUPLICATE URLs (13 measured on the Turkish site).
///   • Slug resolution returns the first match, so one of the two brands could
///     never be opened and its products couldn't be reached from the brand.
///   • It fed Search Console's "Duplicate, Google chose different canonical" issue.
///
/// So normalization lives in ONE PLACE and every multi-brand source goes through it
/// (see ScrapeIngestionService.IngestCoreAsync, which applies it centrally).
///
/// RULE: only different spellings of the SAME manufacturer go here. Merging
/// DIFFERENT manufacturers with similar names would be made-up data.
///
/// The entries below come from the Turkish site's retailers. A name without an
/// entry is returned unchanged, so they are harmless for US stores; add US aliases
/// the same way when a retailer needs them. Differences in letter case, spaces and
/// dots alone don't need an entry: ScrapeIngestionService.FoldBrandName matches
/// those.
/// </summary>
public static class BrandNameNormalizer
{
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The spelling from the brand's own site is canonical: those pages are
            // already indexed.
            ["Protein Ocean"] = "ProteinOcean",
            ["Proteinocean"] = "ProteinOcean",
            ["Big Joy"] = "BigJoy",
            ["Bigjoy"] = "BigJoy",
            ["Swiss"] = "Swiss Nutrition",
            ["Trec Nutrition"] = "Trec",
            ["Universal Nutrition"] = "Universal",
            ["Zero Shot"] = "ZeroShot",
            ["Zeroshot"] = "ZeroShot",
            // The canonical spelling is the one STORED in the database: "Dr Pan".
            // Mapping the other way would create a second brand instead of fixing
            // the existing row (both slugs are "dr-pan", so the URLs would collide).
            ["Dr. Pan"] = "Dr Pan",
            ["Drpan"] = "Dr Pan",
            ["Bite More"] = "Bite & More",
            // One retailer listed the same manufacturer both as "JUST"/"Just" and
            // "FA"/"Fa Nutrition", and each created a separate brand row. The
            // canonical side is always the spelling with more products in the
            // database.
            // The same retailer listed one product on TWO pages, once under
            // "Synergy Nutrition" and once under "Synergy". The manufacturer and the
            // product's ownership were confirmed.
            ["Synergy"] = "Synergy Nutrition",
            ["JUST"] = "Just",
            ["FA Nutrition"] = "Fa Nutrition",
            ["Hiq"] = "HIQ",
            ["Ssn"] = "SSN",

            // A retailer title-cased its brand labels and broke abbreviations. The
            // canonical side is always the spelling in the DATABASE; the right
            // matches were confirmed from the retailer's PRODUCT NAMES, not guessed
            // from the label:
            //   "Konzept" -> its products read "Z-Konzept Isolate Whey ..."
            //   "Optimum" -> its products read "Optimum Gold Standard Whey ..."
            ["Konzept"] = "Z-Konzept",
            ["Optimum"] = "Optimum Nutrition",
            // Culture trap: lowercasing "SIS" with tr-TR gives "Sıs" (dotless ı).
            // The brand's own spelling is SiS (Science in Sport), as in its product
            // names.
            ["Sıs"] = "SiS",
            ["Sis"] = "SiS",
            ["Tnt"] = "TNT",
            ["Gpn"] = "GPN",
            ["Qnt"] = "QNT",
            ["Biotechusa"] = "BioTech USA",

        // A multi-brand store writes schema.org brand names in LOWERCASE ("mla
        // protein", "detoksfit"). Three of them were ALREADY in the catalog from
        // retailers; without mapping to the exact same spelling a duplicate Brand
        // row would be created, which is exactly how duplicates had appeared before.
        ["mla protein"] = "MLA Protein",
        ["Mla Protein"] = "MLA Protein",
        ["Fitnut"] = "FitNut",
        ["Seed'n Grains"] = "Seedn Grains",
        ["Seed’n Grains"] = "Seedn Grains",
        ["detoksfit"] = "Detoksfit",

        // Another retailer: all 14 brands it carried were ALREADY in the catalog;
        // without the exact spelling duplicate Brand rows would be created.
        ["Bigjoy Sports"] = "BigJoy",
        ["Nuclear"] = "Nuclear Nutrition",
        // The dictionary is OrdinalIgnoreCase, which doesn't know the dotted İ, so
        // "KEVİN LEVRONE" does NOT match a "Kevin Levrone" key. The spelling the
        // source uses is therefore added verbatim as the key.
        ["KEVİN LEVRONE"] = "Kevin Levrone",

        // Another retailer: five of its six brands were already in the catalog; it
        // only writes Z-Konzept without the hyphen.
        ["Z Konzept"] = "Z-Konzept",

        // Another retailer carried 54 brand labels, all in UPPERCASE. Compared with
        // the live `Brands` table, 35 were ALREADY in the catalog.
        //
        // MOST OF THEM ARE NOT HERE. Names differing only in letters or spaces
        // ("PRİME NUTRİTİON", "SİS", "TREC", "MEAL JOY") now match through
        // `ScrapeIngestionService.FoldBrandName`; listing them would grow this list
        // by dozens of lines per retailer.
        //
        // The ones below are those folding CAN'T resolve: the name itself differs
        // (the source adds "NUTRITION"/"SPORTS" or drops the hyphen). Unmapped, they
        // would create duplicate brands.
        ["BİG JOY SPORTS"] = "BigJoy",
        ["Z-KONZEPT NUTRİTİON"] = "Z-Konzept",
        ["UNİVERSAL NUTRİTİON"] = "Universal",
        ["Vitargo Nutrition"] = "Vitargo",

        // Brands the same source REALLY introduced. Folding can't map them because
        // they have no counterpart in the catalog: they'd be created for the first
        // time in the source's spelling, i.e. in UPPERCASE. Their canonical spelling
        // is given here so the brand directory doesn't show "MONSTER ENERGY".
        ["ANIMAL JOY"] = "Animal Joy",
        ["PRIME HYDRATION"] = "Prime Hydration",
        ["BİOXLAB"] = "Bioxlab",
        ["MONSTER ENERGY"] = "Monster Energy",
        ["ARMY OF ONE"] = "Army of One",
        ["AEGIS"] = "Aegis",
        ["RULE ONE"] = "Rule One",
        ["DEX SUPPORTS"] = "Dex Supports",

        // One source writes the brand's full name while the catalog holds the short
        // one. That it's the same manufacturer was confirmed from PRODUCT NAMES: the
        // existing "Kingsize" row's products already read "KİNGSİZE NUTRİTİON ALL IN
        // ONE ...". The canonical side is the database spelling (the short one),
        // because renaming a brand breaks its brand page URL.
        ["Kingsize Nutrition"] = "Kingsize",
        };

    /// <summary>
    /// The canonical brand name. An unknown name is returned as is (trimmed);
    /// nothing is guessed.
    /// </summary>
    public static string Normalize(string brandName)
    {
        var trimmed = brandName.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }
}
