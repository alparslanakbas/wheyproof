using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// The filter grows as leaked products are found. The main reason for the tests is
// catching the SIDE EFFECT of growth: an added word can drop a real supplement by
// mistake. The product names are real catalog names from Turkish sources, so most
// are in Turkish.
public class NonSupplementProductFilterTests
{
    [Theory]
    // Apparel / equipment / accessories
    [InlineData("Commander Gold T-Shirt")]
    [InlineData("Commander 700ml Shaker")]
    [InlineData("Commander Havlu")]
    [InlineData("HIQ Hoodie Siyah")]
    [InlineData("Ağırlık Kemeri L")]
    // Food / condiments
    [InlineData("Fit Grains İthal Basmati Pirinç (1000g)")]
    [InlineData("Seed'n Grains Pembe Himalaya Tuzu (250g)")]
    [InlineData("Dr. Pan Bal Aromalı Hardal Şekersiz (260g)")]
    [InlineData("Dr. Pan Sriracha Sos Şekersiz (260g)")]
    [InlineData("Dr.Pan Sweet Drops Lemon Cheesecake (30ml)")]
    public void Drops_non_supplements(string productName)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(productName));
    }

    [Theory]
    // REGRESSION: "rice" (pirinç) must NOT be added to the filter: Cream of Rice is a
    // real sports food sold in the same catalog. A general word would silently drop
    // these products from the site.
    [InlineData("Dr. Pan Rice Cream Çilekli (400g)")]
    [InlineData("Dr. Pan Oat Cream (400g)")]
    [InlineData("HIQ Cream of Rice 1000g")]
    // Real supplements must pass untouched
    [InlineData("Gold Whey Protein 900g (30 Servis)")]
    [InlineData("Creatine Monohydrate Micronized")]
    [InlineData("Overthrow Pre-Workout 375g (25 Servis)")]
    [InlineData("Reload BCAA+ 200g (20 Servis)")]
    [InlineData("Fitnut %100 Badem Ezmesi (Net 250g)")]
    // "performans" (performance) is deliberately not in the filter: real supplement
    // bundles carry the word too.
    [InlineData("Orta Güç Performans Paketi")]
    public void Does_not_drop_real_supplements(string productName)
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(productName));
    }

    [Theory]
    // REGRESSION: the list contained "havlu" (towel) and "çanta" (bag) but these
    // products still went LIVE, because a Turkish suffix shifts the word boundary:
    // the `havlu` pattern didn't match "havlusu".
    [InlineData("Just Profesyonel Antrenman Havlusu (Smart)")]
    [InlineData("Just Leather Sport Bag -Şık Suni Deri Spor Çantası (Kahverengi & Siyah)")]
    [InlineData("Siyah Havlular")]
    [InlineData("Spor Çantaları")]
    [InlineData("Protein Shakerı")]
    public void Accessories_with_turkish_suffixes_are_dropped(string name)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    [Theory]
    // Real supplements must not be affected.
    [InlineData("Whey Protein Tozu 2000 Gr")]
    [InlineData("Creatine Monohydrate 300 Gr")]
    [InlineData("Cream of Rice 1000 Gr")]
    public void Real_supplements_are_not_dropped(string name)
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    [Theory]
    // REGRESSION: these six products went LIVE and a user reported them with a
    // screenshot. Each was a different spelling or suffixed form of a listed word.
    [InlineData("Just Likralı Antrenman Atleti")]
    [InlineData("Just 8 Loop Strap")]
    [InlineData("Protein 7 Pill Box -Tablet Saklama Kabı Aksesuar Protein7 Diğer")]
    [InlineData("Protein 7 Powder Box -Toz Saklama Kabı Aksesuar Protein7 Diğer")]
    [InlineData("Xpro Pill Box -Tablet Saklama Kabı Aksesuar Xpro Nutrition")]
    [InlineData("Antrenman Havlusu 50x90 cm")]
    public void Accessories_that_leaked_live_are_now_dropped(string name)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    [Theory]
    // FALSE POSITIVE GUARD: "Kutu"/"Box" appears in legitimate multi-packs; blind
    // deletion would take these products too.
    [InlineData("Fındıklı Protein Bar 16lı Kutu x 50 gram")]
    [InlineData("SSN Command Quadro Whey 22 Gr x 40 Şase Kutu 880 Gr")]
    [InlineData("SWISS WHEY GOLD DELUXE SERIES SAŞE 24 ADET - 1 Kutu / 24 Servis")]
    [InlineData("PROTEİN BAR KARMA KUTU")]
    // "atletik" (athletic) isn't an accessory; suffix support must not catch it.
    [InlineData("Atletik Performans Kompleksi 90 Kapsül")]
    // The "canta[a-z]*" pattern caught "CANTAloupe", with a real victim in the
    // catalog: a whey protein would have been silently dropped as a gym bag.
    [InlineData("Nois Whey Rex 900G Protein Tozu - Cantaloupe")]
    [InlineData("BCAA Cantaloupe Aromalı")]
    [InlineData("Cantaloupe")]
    public void Legitimate_products_are_not_dropped(string name)
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    // The Cantaloupe fix must not miss real bags: the suffix list was narrowed but
    // coverage kept.
    [Theory]
    [InlineData("Spor Çantası")]
    [InlineData("Hardline Spor Cantasi")]
    [InlineData("Gym Canta")]
    [InlineData("Antrenman Cantalari")]
    public void Real_bags_are_still_dropped(string name)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    // GIFTED SHAKER: a supplement with a gifted accessory, not an accessory. Names from
    // real catalogs.
    [Theory]
    [InlineData("Kilo Aldırıcı Ultra Set - Shaker Hediyeli")]
    [InlineData("HIQ Fitness Başlangıç Paketi + Shaker")]
    [InlineData("HIQ Amino Başlangıç Paketi + Shaker")]
    [InlineData("HIQ Enerji Başlangıç Paketi + Shaker")]
    public void Supplement_bundle_with_gifted_shaker_is_not_dropped(string name)
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    // The exception must be NARROW: if the shaker ITSELF is the product, it's still dropped.
    [Theory]
    [InlineData("Renkli Yüksek Kalite Shaker 550cc")]
    [InlineData("Space Shaker")]
    [InlineData("Prime Nutrition Shaker 500 ml.")]
    [InlineData("Batman Shaker")]
    public void Real_shaker_is_still_dropped(string name)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    // The exception is ONLY for shakers: a bag is a bag even as a gift.
    [Fact]
    public void Gifted_bag_is_still_dropped()
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel("Protein Paketi - Spor Çantası Hediyeli"));
    }

    /// <summary>
    /// Four leaks seen live. The list had the ENGLISH names of apparel (t-shirt,
    /// hoodie, sweatshirt) but not the Turkish ones; when the source wrote Turkish,
    /// none matched.
    /// </summary>
    [Theory]
    [InlineData("Just Raw Edge Series Oversize Kolsuz Kapşonlu")]
    [InlineData("GRİZZONE İMZALI OVERSIZE JOGGERS")]
    [InlineData("Grizzone Joggers")]
    [InlineData("Dijital Ölçü Kaşığı")]
    // Other spellings of the same types that may have leaked: the pattern follows the
    // product TYPE, so it must not be limited to the one form seen.
    [InlineData("Hardline Kapüşonlu Sweatshirt")]
    [InlineData("Space Oversize Tişört")]
    [InlineData("Grizzone Kadın Tayt")]
    [InlineData("Nois Sweatpants Siyah")]
    public void Turkish_apparel_and_accessory_names_are_dropped(string name)
    {
        Assert.True(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }

    /// <summary>
    /// "OVERSIZE" DELIBERATELY stayed out of the pattern: it's a size adjective, not a
    /// product type. It can appear in a mass gainer's name and that product must not be
    /// dropped; real apparel is already caught by TYPE words such as "kolsuz"/"joggers".
    /// </summary>
    [Fact]
    public void Oversize_alone_does_not_drop()
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel("Oversize Mass Gainer 3000 gr"));
    }

    /// <summary>
    /// A real trap found in the false positive scan: "Kap." here abbreviates KAPSÜL
    /// (capsule). A general "kap" pattern would silently drop this supplement, which is
    /// why it stayed out.
    /// </summary>
    [Fact]
    public void Capsule_abbreviation_is_not_dropped()
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(
            "Bağışıklık Paketi-1 (ZMA+Arginine-Multivitamin 90 Kap.)"));
    }

    /// <summary>
    /// The "kapşonlu" (hooded) pattern must not catch capsules: after folding it's
    /// "kapsul" while the pattern is "kap(u)?son...". They don't collide, but the
    /// boundary is pinned by a test.
    /// </summary>
    [Theory]
    [InlineData("Hardline Omega 3 100 Kapsül")]
    [InlineData("Multivitamin 60 Kapsul")]
    public void Capsule_products_are_not_dropped(string name)
    {
        Assert.False(NonSupplementProductFilter.IsAccessoryOrApparel(name));
    }
}
