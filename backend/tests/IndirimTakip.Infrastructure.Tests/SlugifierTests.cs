using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// This class's output must be EXACTLY the same as the frontend's `core/slugify.ts`:
// if the URL reported to IndexNow doesn't match the canonical URL, the notification
// lands on a redirect and loses its value.
//
// The expected values were taken from real URLs on the live Turkish site.
public class SlugifierTests
{
    [Theory]
    // Samples verified against live URLs
    [InlineData("HIQ ALPHA T-MAN 30 CAPS.", "hiq-alpha-t-man-30-caps")]
    [InlineData("Creatine Creapure® 500 Gr", "creatine-creapure-500-gr")]
    [InlineData("HIQ Vitargo Dual Force 1000g", "hiq-vitargo-dual-force-1000g")]
    [InlineData("Pre-Season Fırsatları-1", "pre-season-firsatlari-1")]
    [InlineData("HIQ Bcaa Nrg 390g", "hiq-bcaa-nrg-390g")]
    public void Converts_real_product_names_to_canonical_urls(string name, string expected)
    {
        Assert.Equal(expected, Slugifier.Slugify(name));
    }

    [Theory]
    // Turkish letters caused bugs three times (with tr-TR an uppercase I becomes a
    // dotless ı, with ToLowerInvariant an uppercase İ isn't lowercased at all).
    [InlineData("Çikolatalı Protein Bar", "cikolatali-protein-bar")]
    [InlineData("ÜZÜM AROMALI", "uzum-aromali")]
    [InlineData("İZOLE WHEY", "izole-whey")]
    [InlineData("Şeftali & Ğ Testi", "seftali-g-testi")]
    [InlineData("KREATİN MİKRONİZE", "kreatin-mikronize")]
    public void Maps_turkish_letters_correctly(string name, string expected)
    {
        Assert.Equal(expected, Slugifier.Slugify(name));
    }

    [Theory]
    [InlineData("  Leading and trailing space  ", "leading-and-trailing-space")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Punctuation!!! ??? ...", "punctuation")]
    [InlineData("100% Pure & Natural", "100-pure-natural")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Cleans_punctuation_and_whitespace(string name, string expected)
    {
        Assert.Equal(expected, Slugifier.Slugify(name));
    }

    [Fact]
    public void Truncates_long_names_without_cutting_a_word()
    {
        var longName = "SSN Whey Refuel 1800g Chocolate Plus SSN Creatine 300g Plus SSN Glutamine 300g Combination Bundle";
        var slug = Slugifier.Slugify(longName);

        Assert.True(slug.Length <= 80);
        // Must not cut mid-word: no half word left at the end.
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.EndsWith('-'));
    }
}
