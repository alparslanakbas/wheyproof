using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Brand name normalization isn't cosmetic: two spellings resolve to the same URL
/// (the brand slug is lowercased), so without merging, the sitemap gets duplicate URLs
/// and one of the brands can never be opened.
/// </summary>
public class BrandNameNormalizerTests
{
    [Theory]
    [InlineData("Proteinocean")]
    [InlineData("Protein Ocean")]
    [InlineData("ProteinOcean")]
    public void Spellings_of_one_manufacturer_reduce_to_one_name(string spelling)
    {
        Assert.Equal("ProteinOcean", BrandNameNormalizer.Normalize(spelling));
    }

    [Theory]
    [InlineData("Big Joy", "BigJoy")]
    [InlineData("Bigjoy", "BigJoy")]
    [InlineData("Swiss", "Swiss Nutrition")]
    [InlineData("Zero Shot", "ZeroShot")]
    [InlineData("Trec Nutrition", "Trec")]
    public void Known_aliases_map_to_the_canonical_name(string raw, string expected)
    {
        Assert.Equal(expected, BrandNameNormalizer.Normalize(raw));
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed()
    {
        Assert.Equal("ProteinOcean", BrandNameNormalizer.Normalize("  Proteinocean  "));
    }

    [Theory]
    [InlineData("Olimp")]
    [InlineData("Mustang Nutrition")]
    [InlineData("Grenade")]
    public void Unknown_brand_stays_as_is(string name)
    {
        // Nothing is guessed: merging DIFFERENT manufacturers with similar names would
        // be made-up data.
        Assert.Equal(name, BrandNameNormalizer.Normalize(name));
    }

    [Theory]
    // From a retailer scrape: the same manufacturer came in with two spellings and
    // created two brand rows.
    [InlineData("JUST", "Just")]
    [InlineData("just", "Just")]
    [InlineData("FA Nutrition", "Fa Nutrition")]
    [InlineData("Bite More", "Bite & More")]
    [InlineData("Synergy", "Synergy Nutrition")]
    public void Retailer_duplicates_merge(string raw, string expected)
    {
        Assert.Equal(expected, BrandNameNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData("Dr. Pan")]
    [InlineData("Drpan")]
    public void Canonical_spelling_is_the_database_spelling(string raw)
    {
        // Mapping the other way would create a SECOND brand instead of fixing the
        // existing row; both slugs are "dr-pan", so the URLs would collide.
        Assert.Equal("Dr Pan", BrandNameNormalizer.Normalize(raw));
    }

    // A retailer title-cased its brand labels and broke abbreviations. The right
    // matches were confirmed from the retailer's PRODUCT NAMES, not guessed.
    [Theory]
    [InlineData("Konzept", "Z-Konzept")]        // its products read "Z-Konzept Isolate Whey"
    [InlineData("Optimum", "Optimum Nutrition")] // its products read "Optimum Gold Standard"
    [InlineData("Tnt", "TNT")]
    [InlineData("Gpn", "GPN")]
    [InlineData("Qnt", "QNT")]
    [InlineData("Biotechusa", "BioTech USA")]
    public void Title_cased_retailer_labels_map_to_canonical_spelling(string input, string expected)
    {
        Assert.Equal(expected, BrandNameNormalizer.Normalize(input));
    }

    // Culture trap: lowercasing "SIS" with tr-TR gives "Sıs" with a dotless ı. The
    // brand's own spelling is SiS (Science in Sport).
    [Theory]
    [InlineData("Sıs")]
    [InlineData("Sis")]
    public void SiS_broken_by_the_dotless_i_is_fixed(string input)
    {
        Assert.Equal("SiS", BrandNameNormalizer.Normalize(input));
    }

    // Names folding CAN'T resolve: the source adds a word to the brand name or drops
    // the hyphen, so it isn't a letter difference.
    [Theory]
    [InlineData("BİG JOY SPORTS", "BigJoy")]
    [InlineData("Z-KONZEPT NUTRİTİON", "Z-Konzept")]
    [InlineData("UNİVERSAL NUTRİTİON", "Universal")]
    [InlineData("Vitargo Nutrition", "Vitargo")]
    [InlineData("Z Konzept", "Z-Konzept")]
    [InlineData("Kingsize Nutrition", "Kingsize")]
    public void Retailer_labels_with_extra_words_become_canonical(string input, string expected)
    {
        Assert.Equal(expected, BrandNameNormalizer.Normalize(input));
    }

    // Brands a source REALLY introduces arrive in uppercase; with no counterpart in the
    // catalog folding can't help, and they would be stored as "MONSTER ENERGY".
    [Theory]
    [InlineData("MONSTER ENERGY", "Monster Energy")]
    [InlineData("ANIMAL JOY", "Animal Joy")]
    [InlineData("BİOXLAB", "Bioxlab")]
    [InlineData("RULE ONE", "Rule One")]
    public void New_brands_are_stored_with_canonical_spelling(string input, string expected)
    {
        Assert.Equal(expected, BrandNameNormalizer.Normalize(input));
    }

    // Pure letter/space differences DON'T go here, which keeps the alias list from
    // growing. Normalize returns them as is; matching happens in
    // ScrapeIngestionService.FoldBrandName (see BrandNameFoldingTests).
    [Theory]
    [InlineData("TREC")]
    [InlineData("CELLUCOR")]
    [InlineData("PRİME NUTRİTİON")]
    public void Letter_differences_are_not_in_the_alias_list(string input)
    {
        Assert.Equal(input, BrandNameNormalizer.Normalize(input));
    }
}
