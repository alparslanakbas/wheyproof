using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// The letter/space layer of brand matching. Without these tests every new retailer
/// would create DUPLICATE brands in the catalog: the brand cache is ordinal and
/// case-sensitive, so "TREC" and "Trec" were treated as different.
/// </summary>
public class BrandNameFoldingTests
{
    [Theory]
    // Letter case only.
    [InlineData("TREC", "Trec")]
    [InlineData("CELLUCOR", "Cellucor")]
    [InlineData("ZOOMAD LABS", "Zoomad Labs")]
    [InlineData("JNX Sports", "Jnx Sports")]
    [InlineData("DY NUTRITION", "DY Nutrition")]
    [InlineData("ON THE GO", "On The Go")]
    // Turkish DOTTED İ: .NET's culture-independent comparison doesn't fold it to "i",
    // so none of these rows would match on their own.
    [InlineData("PRİME NUTRİTİON", "Prime Nutrition")]
    [InlineData("APPLİED NUTRİTİON", "Applied Nutrition")]
    [InlineData("EFFİVE NUTRİTİON", "Effive Nutrition")]
    [InlineData("KİNGSİZE", "Kingsize")]
    [InlineData("ENERVİT", "Enervit")]
    [InlineData("DYMATİZE", "Dymatize")]
    [InlineData("SİS", "SiS")]
    [InlineData("BİTE & MORE", "Bite & More")]
    // Spaces and dots.
    [InlineData("MEAL JOY", "Mealjoy")]
    [InlineData("Dr. Pan", "Dr Pan")]
    [InlineData("Big Joy", "BigJoy")]
    public void Spellings_of_the_same_brand_fall_into_one_bucket(string a, string b)
        => Assert.Equal(ScrapeIngestionService.FoldBrandName(a), ScrapeIngestionService.FoldBrandName(b));

    [Theory]
    // Different manufacturers must not merge.
    [InlineData("Prime Nutrition", "Prime Hydration")]
    [InlineData("Z-Konzept", "Zkonzept")]   // the hyphen is kept on purpose
    [InlineData("Nuclear Nutrition", "Nuclear")]
    [InlineData("BigJoy", "Big Joy Sports")]
    public void Different_names_stay_apart(string a, string b)
        => Assert.NotEqual(ScrapeIngestionService.FoldBrandName(a), ScrapeIngestionService.FoldBrandName(b));

    [Fact]
    public void All_four_turkish_i_letters_fold_to_one_character()
    {
        // I ı İ i must all land in one bucket.
        Assert.Equal("iiii", ScrapeIngestionService.FoldBrandName("Iıİi"));
    }
}
