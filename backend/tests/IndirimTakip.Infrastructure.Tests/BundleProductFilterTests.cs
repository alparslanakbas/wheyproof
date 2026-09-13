using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// All names come from real Turkish catalogs; the filter matches the Turkish words for
// bundle ("paketi") and set ("seti").
public class BundleProductFilterTests
{
    [Theory]
    [InlineData("Spor Rutini Paketi")]
    [InlineData("Kas Desteği Paketi")]
    [InlineData("Cilt Sağlığı Paketi")]
    // The "seti" form
    [InlineData("4’lü Deneme Seti – Kolajen & C Vitamini")]
    [InlineData("Sporcu Paketi")]
    [InlineData("Eklem Sağlığı Paketi")]
    // All-uppercase spelling
    [InlineData("FITNESS PAKETİ")]
    [InlineData("FITNESS PAKETİ - MEGA")]
    public void Recognizes_multi_product_sets(string name)
    {
        Assert.True(BundleProductFilter.IsBundle(name));
    }

    [Theory]
    [InlineData("WHEY Protein Isolate - Çikolata Aromalı")]
    [InlineData("Creatine MonoHydrate – 510 g (100 servis)")]
    [InlineData("Marine Collagen (Tablet Form)")]
    [InlineData("Multi Magnezyum Complex")]
    [InlineData("Vitamin B12 1000 MCG - 100 Tablet")]
    public void Does_not_affect_real_products(string name)
    {
        Assert.False(BundleProductFilter.IsBundle(name));
    }

    // Letter case trap: the dotted İ in "PAKETİ" doesn't fold to "i" with
    // OrdinalIgnoreCase; without normalization all-uppercase names would slip through.
    [Theory]
    [InlineData("KREATİN PAKETİ")]
    [InlineData("Kreatin Paketi")]
    [InlineData("kreatin paketi")]
    [InlineData("DENEME SETİ")]
    public void Is_not_affected_by_case_or_the_dotted_i(string name)
    {
        Assert.True(BundleProductFilter.IsBundle(name));
    }

    // The pattern is NARROW: the "set"/"paket" stem must be the word itself, not an
    // arbitrary substring.
    [Theory]
    [InlineData("Reset Formula")]
    [InlineData("Beta Alanin Preset")]
    [InlineData("Korseti")]
    public void Set_inside_another_word_is_not_caught(string name)
    {
        Assert.False(BundleProductFilter.IsBundle(name));
    }

    // A real catalog product: a SINGLE-serving pre-workout ("Tek Paket Servis" = single
    // packet serving), NOT a set. A bare "paket" pattern would silently drop it.
    [Fact]
    public void Single_packet_serving_is_not_a_set()
    {
        Assert.False(BundleProductFilter.IsBundle(
            "Buster Preworkout L-Arjinin & Beta- Alanin 22.4 gram Tek Paket Servis"));
    }
}
