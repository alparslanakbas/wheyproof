using System.Globalization;
using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

// Marka scraper'ları ürün ismini olduğu gibi veriyor (örn. "SSN ... 2100 Gr
// (Bisküvi) Protein Tozu"). Boyut, aroma ve (eksikse) kategori bilgisini bu
// tek isimden çıkarıyoruz — böylece 4 farklı sitede ayrı ayrı parse mantığı
// yazmak yerine tüm markalar için tek bir yerde çözülüyor. Ayrıca arama
// kutusunun markadan bağımsız çalışması da buna dayanıyor (bkz. Category).
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

    // Aroma sözlüğü. "Parantez içini ya da tireden sonrasını aroma say" gibi
    // biçimsel bir kural GERÇEK VERİYLE ELENDİ: canlıda dolu olan 80 Flavor
    // değerinin 42'si aroma değildi — porsiyon/miktar ("40 Servis",
    // "15 x 4 Doypacks", "1000 IU"), paket içeriği ("EAA + HellFire
    // Pre-Workout") ve etken madde ("Arginine", "Collagen", "Maca") alana
    // yazılmıştı. Bu alan kullanıcıya "Aroma: 40 Servis" olarak gösteriliyor
    // ve DealsQueryService'te ARAMAYA da dahil, yani yanlış değer hem
    // görünüyor hem eşleşiyordu.
    //
    // Bu yüzden kural tersine çevrildi: aday ancak bilinen bir aroma
    // kelimesiyle eşleşirse kabul ediliyor. Bilinmeyen yeni bir aroma boş
    // kalır — bilinçli takas, "uydurma veri yok" kuralıyla aynı yönde:
    // yanlış göstermektense boş bırak.
    //
    // Liste uydurulmadı, canlı veriden çıkarıldı. İngilizce yazımlar da var
    // çünkü markalar karışık kullanıyor (veride "Creme Caramel" bulundu).
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

    // Birden fazla kelimeden oluşanlar ayrı: bunlar token eşleşmesiyle değil
    // doğrudan aranıyor.
    private static readonly string[] FlavorPhrases =
    [
        "hindistan cevizi", "yaban mersini", "orman meyve",
    ];

    /// <summary>
    /// Türkçe ünsüz yumuşaması: ek alan kelimenin son sessizi değişiyor
    /// (çilek → çileği, kitap → kitabı). Sadece "çilek" ile başlayanlara
    /// bakılsaydı "Ereğli Çileği" elenirdi — Yeşilmarka'nın gerçek bir
    /// ürünü. Bu yüzden her aroma kelimesinin yumuşamış gövdesi de
    /// eşleştirmeye giriyor.
    /// </summary>
    private static string SoftenFinalConsonant(string word) => word.Length == 0 ? word : word[^1] switch
    {
        'k' => string.Concat(word.AsSpan(0, word.Length - 1), "ğ"),
        'p' => string.Concat(word.AsSpan(0, word.Length - 1), "b"),
        't' => string.Concat(word.AsSpan(0, word.Length - 1), "d"),
        'ç' => string.Concat(word.AsSpan(0, word.Length - 1), "c"),
        _ => word,
    };

    // Eşleştirmede kullanılan gövdeler: sözlüğün kendisi + yumuşamış hâlleri.
    private static readonly string[] FlavorStems =
        [.. FlavorWords.Concat(FlavorWords.Select(SoftenFinalConsonant)).Distinct()];

    /// <summary>
    /// Türkçe harf tuzağı için normalleştirme. "AROMASIZ" ToLowerInvariant
    /// ile "aromasiz" oluyor ama sözlükteki "aromasız" noktasız ı taşıyor;
    /// "ÇİLEK" ise noktalı İ yüzünden invariant kültürde hiç küçülmüyor.
    /// Noktalı/noktasız ayrımı iki tarafta da siliniyor.
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

        // İkinci kaynak: " - Aroma" son eki. Yeşilmarka'nın mağaza API'si her
        // aromayı ayrı ürün olarak döndürüyor ve aromayı ismin sonuna
        // koyuyor ("BCAA 4:1:1 - Ananas"); parantez hiç kullanmıyor.
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

        // Rakam taşıyan aday neredeyse her zaman miktar/porsiyon bilgisidir.
        // Canlı veride rakam içeren tek bir gerçek aroma yok.
        if (candidate.Any(char.IsDigit))
            return false;

        // "+" paket içeriği, "®"/"™" marka adı işaretidir.
        if (candidate.Contains('+') || candidate.Contains('®') || candidate.Contains('™'))
            return false;

        if (SizeRegex().IsMatch(candidate))
            return false;

        var normalized = NormalizeForFlavorMatch(candidate);

        if (FlavorPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal)))
            return true;

        // Kelime bazlı eşleşme: "içeriyor mu" yerine "hangi kelimeyle
        // BAŞLIYOR". Böylece Türkçe ekler yakalanıyor ("çilekli", "muzlu",
        // "limonlu") ama kısa kelimeler ("bal", "nar") başka bir kelimenin
        // ortasına denk gelip yanlış eşleşmiyor.
        var tokens = normalized.Split([' ', '-', '/', ',', '.', '&', '(', ')', '*'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => FlavorStems.Any(stem => token.StartsWith(stem, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Marka adının ürün adı içindeki geçişlerini siler. Marka bilinmiyorsa
    /// ad olduğu gibi döner.
    ///
    /// Not: karşılaştırma OrdinalIgnoreCase — ASCII marka adlarında ("BigJoy",
    /// "Proteinocean") doğru çalışıyor. Türkçe harf içeren marka adlarında
    /// (noktalı/noktasız i) eşleşmeyebilir; o markalarda bu sorun gözlenmedi,
    /// gerekirse normalleştirme eklenir.
    /// </summary>
    private static string StripBrandName(string productName, string? brandName)
    {
        if (string.IsNullOrWhiteSpace(brandName))
            return productName;

        var stripped = productName.Replace(brandName, " ", StringComparison.OrdinalIgnoreCase);

        // Marka adı boşluksuz da yazılabiliyor ("Proteinocean" / "Protein Ocean").
        var compact = brandName.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length > 3 && compact.Length != brandName.Length)
            stripped = stripped.Replace(compact, " ", StringComparison.OrdinalIgnoreCase);

        // Adın tamamı markadan ibaretse çıkarım yapacak bir şey kalmıyor;
        // orijinali döndürmek yanlış kategoriden iyidir.
        return stripped.Trim().Length == 0 ? productName : stripped;
    }

    /// <summary>
    /// Ürün adından kategori çıkarımı.
    /// </summary>
    /// <param name="brandName">
    /// Biliniyorsa üretici markası; ad içinden ÇIKARILIYOR. Bayi kaynakları
    /// ürün adına markayı da yazıyor ("Proteinocean Creatine 300gr Kreatin
    /// Monohidrat") ve marka adı bir kategori anahtar kelimesi içeriyorsa
    /// ürün yanlış kategoriye düşüyor: gerçek veride ProteinOcean'ın
    /// kreatini, omega'sı ve vitamini "protein tozu" olarak kaydedilmişti,
    /// çünkü "Protein-ocean" içindeki "protein" eşleşiyordu.
    ///
    /// Kategori ürünün NE OLDUĞUNDAN çıkarılmalı, kimin ürettiğinden değil.
    /// </param>
    public static string? InferCategory(string productName, string? brandName = null)
    {
        productName = StripBrandName(productName, brandName);

        // ToLowerInvariant bilinçli — tr-TR kültüründe büyük "I" küçülünce
        // noktasız "ı" oluyor ("CREATINE" -> "creatıne"), bu da aşağıdaki
        // İngilizce anahtar kelimelerle ("creatine" gibi) hiç eşleşmiyordu.
        // İkinci, ayrı bir tuzak daha var: Türkçe büyük noktalı "İ"
        // (ör. "C VİTAMİNİ") ToLowerInvariant ile HİÇ küçülmüyor (invariant
        // kültürde bu harf için basit bir eşleme yok) — "vİtamİnİ" olarak
        // kalıp "vitamin" anahtar kelimesiyle asla eşleşmiyordu (canlı veride
        // yüzlerce ürünün kategorisiz kalmasının gerçek sebeplerinden biriydi).
        // Elle .Replace ile düzeltiliyor, culture-sensitive ToLower'a dönmeden.
        var normalized = productName.Replace('İ', 'i').ToLowerInvariant();

        // ÜRÜNÜN BİÇİMİ, İÇERİĞİNDEN ÖNCE GELİR. Anahtar kelime listesi
        // sırayla taranıyor ve "protein-tozu" en başta; bu yüzden adında
        // "protein" geçen bir BAR, toz kategorisine düşüyordu. Canlı veride
        // ölçüldü: 11 markada 39 protein barı "protein-tozu" etiketliydi
        // (Multipower, Musclestation, Grenade, HIQ, Hardline, Torq...),
        // 15 tanesi ise doğru kategorideydi — yani aynı ürün tipi iki
        // kategoriye bölünmüştü.
        //
        // Kelime sınırı ŞART: liste "bar" alt dizisini arıyordu ve
        // "Barbekü Baharatı" bu yüzden atıştırmalık sayılıyordu.
        if (SnackBarFormRegex().IsMatch(normalized))
            return "protein-snacks";

        foreach (var (category, pattern) in CategoryPatterns)
        {
            if (pattern.IsMatch(normalized))
                return category;
        }

        return null;
    }

    // Arama kutusu için eşanlamlı gruplar — CategoryKeywords'ten BİLİNÇLİ
    // OLARAK AYRI bir yapı. CategoryKeywords listeleri KATEGORİ TESPİTİ için
    // doğru (bir ürünün hangi kategoriye ait olduğunu belirlemek için geniş/
    // heterojen bir kelime havuzu gerekiyor — "vitamin" kategorisinde 35+
    // birbiriyle alakasız bileşen olması kategori tespiti açısından sorun
    // değil). Ama bu geniş listeleri ARAMA EŞANLAMLISI olarak kullanmak
    // (kullanıcı bir kelime yazınca TÜM kategoriyi eşanlamlı saymak) yanlış
    // sonuç veriyordu — ilk bulgu 2026-08-24: "magnezyum" araması "vitamin"
    // kategorisinin tamamını (NMN, ZMA, Biotin dahil, hiçbiri magnezyumla
    // ilgisi olmayan) eşanlamlı sayıp en üste çıkarıyordu. Kullanıcı sorunca
    // aynı deseni TÜM kategorilerde kontrol ettik — "amino-asitler" (16
    // kelime) ve "kilo-hacim" (10 kelime) de aynı şekilde bozuktu (ör.
    // "taurine"/"glutamin"/"arginin" aramalarının HEPSİ aynı 83 ürünü, aynı
    // sırayla döndürdüğü doğrulandı).
    //
    // Çözüm: her kategori için CategoryKeywords'ü OLDUĞU GİBİ bırakıp (kategori
    // tespiti hiç etkilenmiyor), SADECE gerçekten aynı kavramın farklı yazımı/
    // dili/markası olan DAR alt-grupları burada ayrıca tanımlıyoruz. Aynı
    // kategorideki ama birbirinden farklı bileşenler (ör. "glycine" ve
    // "taurine", ikisi de amino-asitler ama biri diğerinin eşanlamlısı değil)
    // BİLİNÇLİ OLARAK hiçbir grupta yer almıyor — kendi başlarına aranıyorlar.
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

    // Markanın kendi ürün açıklamasından porsiyon (servis) büyüklüğünü
    // çıkarır. HIQ'da bu bilgi zaten yapısal olarak (Shopify'ın besin değeri
    // tablosundan) geliyordu ama diğer 3 markada hiç yoktu — açıklamalar
    // çekilmeye başlandıktan sonra bu bilginin metnin içinde ("1 ölçek (30 g)",
    // "Porsiyon Büyüklüğü: 25 g", "Servis başına 23 g" gibi) serbest formda
    // durduğu görüldü. Dört farklı yazım kalıbı deneniyor; hiçbiri tutmazsa
    // null dönüyor (tahmin/varsayım YOK — "30 gr = 1 servis" gibi bir kabul
    // bu projede bilinçli olarak hiç yapılmadı).
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

            // Makul olmayan eşleşmeleri ele: gerçek veride en küçük porsiyonlar
            // tekil amino asitlerde 1 g (Citrulline/Glycine), en büyükleri
            // gainer'larda 200 g civarı. Bunun dışına taşan bir sayı, metinde
            // porsiyonla ilgisiz bir yerden yakalanmış demektir (ör. bir sos
            // ürününde 0,22 g).
            if (grams is >= 1m and <= 500m)
                return grams;
        }

        return null;
    }

    // Sıra önemli: en açık/az yanılabilir kalıptan başlıyor ("Porsiyon
    // Büyüklüğü: 30 g"), en sonda daha gevşek olan geliyor.
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
    /// Ürünün bar biçiminde olduğunu söyleyen kelime. Türkçe ekler dahil
    /// ("barı", "barlar"), ama "Barbekü"/"Barbell" gibi kelimelerin içine
    /// denk gelmemesi için kelime sınırıyla.
    /// </summary>
    [GeneratedRegex(@"\b(bars?|cookies?|chips|crisps|puffs|brownies?|wafers?|pretzels?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SnackBarFormRegex();

    [GeneratedRegex(@"(?<count>\d+)\s*servings?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServingsCountRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
