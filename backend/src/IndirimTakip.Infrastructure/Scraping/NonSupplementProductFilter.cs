using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

// Name-based accessory/apparel filter. Some sources attach no structured
// category or tag to products (no category at all, or a single "All Products"
// category), so a tag-based filter isn't possible and a word list is used
// instead. The site covers sports supplements and protein; apparel and
// accessories such as t-shirts, hoodies, caps, keychains, funnels and pill boxes
// are out of scope.
//
// The words are mostly Turkish, because the filter was written for Turkish
// sources. Shopify stores get the English apparel pattern and the structural
// option checks in ShopifyStoreScraper on top of this.
public static partial class NonSupplementProductFilter
{
    public static bool IsAccessoryOrApparel(string productName)
    {
        // TURKISH LETTERS ARE FOLDED TO ASCII, BEFORE the patterns run.
        //
        // .NET's RegexOptions.IgnoreCase works with the invariant culture and doesn't
        // fold Turkish letter pairs. The result: a product named "EFFİVE BLACK
        // PİLLBOX" did NOT match the `pillbox` pattern and stayed in the live catalog
        // as an accessory. The dotless I has the same trap: all-caps names such as
        // "ANAHTARLIK" (keychain) didn't match the `anahtarlık` pattern, and one
        // store's catalog was uppercase from start to end.
        //
        // That's why the patterns are written in PLAIN ASCII. Before, an ASCII copy of
        // every Turkish word was kept separately; with one form that duplication
        // became unnecessary and missing spellings were closed too.
        productName = FoldTurkishToAscii(productName);

        var match = AccessoryKeywordRegex().Match(productName);
        if (!match.Success)
            return false;

        // GIFTED SHAKER EXCEPTION. Brands give a shaker with supplement bundles and
        // say so in the name ("... Set - Shaker Hediyeli" = shaker included, "...
        // Paketi + Shaker"). These aren't accessories but supplements WITH a gifted
        // accessory; dropping them loses real products.
        //
        // The catalog was scanned (1,456 names): all THREE products containing
        // "shaker" were of this kind, with no real shaker among them (those are
        // dropped at ingestion anyway). Real shakers never carry these markers.
        //
        // The exception is NARROW: it applies only when "shaker" is the single
        // matched word and the name has a gift/addition marker. A name like "Spor
        // Çantası Hediyeli" (gym bag included) is still dropped, because the matched
        // word there is "çanta" (bag).

        // SUPPLEMENT BUNDLE EXCEPTION. Products whose name has both a BUNDLE/SET marker
        // and a supplement ingredient must not be dropped because of the accessories
        // inside: those accessories aren't the product ITSELF but the bundle's
        // contents.
        //
        // Found by measuring one store's catalog; the filter was dropping three REAL
        // supplement bundles:
        //   "FİTNESS PAKETİ - PROFESYONEL (WHEY PROTEİN PRO 1800 GR + ...)"
        //      -> tripped on shaker, towel, strap and keychain words
        //   "GRIZZY IRON PACK (WHEY PROTEIN PRO + ZINC+D3+C + GRIZZONE SHAKER)"
        //   "FİTNESS PAKETİ - ORTA (WHEY PROTEIN 420 GR + BCAA 500 GR + ...)"
        //
        // The gifted shaker exception below did NOT save them: it applies only with a
        // SINGLE match and "+shaker" written together.
        //
        // Both conditions are required, because each alone is insufficient: "Grizzone
        // SACKPACK Spor Çanta" contains "pack" (the word boundary rules it out) and a
        // food bundle "(PANCAKE + ... SOS)" carries a bundle marker but no supplement
        // ingredient; both are correctly dropped.
        //
        // The exception does NOT EXTEND to apparel and bags. The standing rule: "a bag
        // is a bag even as a gift" (see the gifted-bag test). The rescued bundles
        // contained shakers, towels, straps and keychains, typical gift items; in a
        // name with apparel or a bag the bundle exception doesn't apply and the product
        // is still dropped.
        if (BundleMarkerRegex().IsMatch(productName)
            && SupplementMarkerRegex().IsMatch(productName)
            && !ApparelOrBagRegex().IsMatch(productName))
        {
            return false;
        }

        var onlyShaker = match.Value.StartsWith("shaker", StringComparison.OrdinalIgnoreCase)
            && AccessoryKeywordRegex().Matches(productName).Count == 1;

        return !(onlyShaker && GiftedAccessoryRegex().IsMatch(productName));
    }

    // The list grows as leaked products are found; every addition below was made
    // after measuring a real catalog, not by guessing. Turkish words and what they
    // mean: tisort (t-shirt), kapuson (hood), kolsuz (sleeveless), tayt (leggings),
    // sapka (cap), bileklik (wristband), havlu (towel), atlet (tank top), anahtarlik
    // (keychain), huni (funnel), sort (shorts), korse (lifting belt), esofman
    // (tracksuit), canta (bag), direnc band (resistance band), agirlik kemer
    // (weightlifting belt), eldiven (gloves), hap kutusu (pill box), olcu/olcek kasigi
    // (measuring scoop), bakim/seyahat seti (grooming/travel kit), kase (bowl), kuru
    // yemislik (nut bowl), himalaya tuzu (Himalayan salt), hardal (mustard), sos
    // (sauce), ketcap (ketchup), sprey yag (cooking spray), tatlandirici (sweetener).
    //
    // A food/condiment group is included (basmati, Himalayan salt, mustard, sriracha,
    // sweet drops): one store also sold rice, salt, sauces and liquid sweeteners,
    // which aren't sports supplements.
    //
    // "rice" is DELIBERATELY NOT in the list: "Cream of Rice" is a real sports food in
    // the same catalog. Only distinctive words (such as basmati) are used instead of
    // the general one. General words like "performans" (performance) are absent for
    // the same reason: real supplement bundles carry that word too.
    //
    // When a new retailer catalog is added, run this query and review the result by
    // eye:
    //   SELECT "Name" FROM "Products" WHERE lower("Name") ~ '(atlet|havlu|çanta|strap|box|kemer|...)';
    // Blind deletion must be avoided: "Kutu" (box) also appears in legitimate
    // multi-packs ("Protein Bar 16lı Kutu").
    //
    // Leaks that shaped the patterns: "Atleti" slipped through because suffix
    // support was added to "havlu"/"canta" but not "atlet"; "8 Loop Strap" because
    // the pattern was only "lifting strap"; "Pill Box"/"Powder Box" because only the
    // joined "pillbox" was listed. Lesson: write the pattern for the product's TYPE,
    // not the one spelling you saw.
    //
    // "atlet" is deliberately NOT written with an open suffix: that form would also
    // catch "atletik" (athletic), and "atletik performans" is a legitimate supplement
    // phrase.
    //
    // TURKISH SUFFIXES: `\b` doesn't break BEFORE a suffix, so "havlu" didn't match
    // "havlusu". Nouns that take suffixes got `[a-z]*`, except where an open suffix
    // hits other words: "canta[a-z]*" caught "Cantaloupe" and a real whey protein in
    // Cantaloupe flavor would have been dropped silently as a gym bag. Those use an
    // EXPLICIT SUFFIX LIST ("canta(si|lar|lari)?", "atlet(i|ler|leri)?").
    //
    // "sos" uses a word boundary and an explicit suffix list, "sos(u|lar|lari)?": it
    // does NOT catch "SOSis" (sausage), because the following "is" isn't in the list
    // and the boundary doesn't hold. The same reasoning applies to "kase".
    //
    // "Flavor Chocolate" was DELIBERATELY not added: it appeared in one product, and
    // general words like "flavor/chocolate" would drop real flavored products. Missing
    // a condiment beats dropping a protein powder.
    //
    // THE APPAREL GROUP WAS MISSING ITS TURKISH WORDS. A sleeveless hoodie ("Kolsuz
    // Kapşonlu") showed up live: the list had English apparel names (t-shirt, hoodie,
    // sweatshirt) but no Turkish counterparts. The catalog scan found four leaks,
    // joggers and a measuring scoop among them.
    //
    // "OVERSIZE" was DELIBERATELY NOT added although two leaks carried it: it's a size
    // adjective, not a product type, and a mass gainer's name could contain it. Both
    // products are dropped by "kolsuz"/"joggers" anyway, so the pattern looks at the
    // product's TYPE, not the adjective.
    //
    // A FALSE POSITIVE SCAN RAN FIRST (all 4,918 names): the candidate words caught
    // only those four products. The scan also showed a real trap that stayed OUT of
    // the pattern: "... Multivitamin 90 Kap." where "Kap." abbreviates KAPSÜL
    // (capsule). A general "kap" pattern would silently drop a real supplement (it has
    // a test).
    // GYM EQUIPMENT (added 2026-09-18 with the DMoose store). A pull-up bar was
    // categorised as "protein-snacks" because its name contains "bar": the category
    // rule alone would have let four equipment rows onto the site. Written for the
    // product TYPE, as the rest of this pattern is. The words were checked against
    // all 4,570 live product names first: zero matches, so none of them drops a
    // real supplement.
    // CHECKOUT ADD-ONS (added 2026-09-19 with the UK stores). Bodybuilding
    // Warehouse lists its shipping insurance, "ProtectMyOrder", as products at
    // £1.09 and £1.39: just above the price floor that catches the same kind of
    // row elsewhere (Ascent's "Shipping Protection" at $0.75). Whole phrases
    // only: "protection" alone could sit in a real supplement's name.
    [GeneratedRegex(
        @"\b(protect ?my ?order|shipping protection|package protection|order protection|pull ?-? ?up bar|chin ?-? ?up bar|dip bar[s]?|push ?-? ?up bar[s]?|ab roller|ab wheel|jump rope|speed rope|dumbbell[s]?|barbell[s]?|kettlebell[s]?|weight plate[s]?|wrist roller|forearm roller|arm twister|arm trainer|resistance band[s]?|lifting kit|weightlifting hook[s]?|lifting grip[s]?|knee sleeve[s]?|elbow sleeve[s]?|lifting belt|weightlifting belt|gym bag|duffle bag|backpack|water bottle|insulated bottle|t-?shirt|tisort[a-z]*|sweatshirt|sweatpant[a-z]*|hoodie|kap[u]?son[a-z]*|kolsuz|jogger[a-z]*|tayt|legging[a-z]*|sapka[a-z]*|beyzbol|pillbox|pill ?box|powder ?box|saklama kabi|bileklik[a-z]*|havlu[a-z]*|buff|atlet(i|ler|leri)?|anahtarlik[a-z]*|maskot|huni[a-z]*|shaker[a-z]*|sort[a-z]*|korse[a-z]*|esofman[a-z]*|canta(si|lar|lari)?|handbag|direnc band[a-z]*|loop band[a-z]*|strap[a-z]*|wrist wrap[a-z]*|agirlik kemer[a-z]*|dip belt[a-z]*|eldiven[a-z]*|hap kutusu|olcu kasig[a-z]*|olcek kasig[a-z]*|bakim seti|seyahat seti|kase(si|ler|leri)?|kuru yemislik|basmati|himalaya tuzu|hardal|sriracha|sweet drops|sos(u|lar|lari)?|ketcap|ketchup|garlic powder|hot chili|cajun|chicken mix|vegetable mix|bbq|sprey yag[i]?|tatlandirici)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AccessoryKeywordRegex();

    /// <summary>
    /// "Hediyeli" (gift included) / "+ Shaker": markers saying the accessory isn't
    /// the product ITSELF but an extra given with it. No dotted İ trap (all ASCII
    /// letters), IgnoreCase is enough.
    /// </summary>
    [GeneratedRegex(@"(hediye[a-z]*|\+\s*shaker)", RegexOptions.IgnoreCase)]
    private static partial Regex GiftedAccessoryRegex();

    /// <summary>
    /// Bundle/set marker (paket = bundle). WORD-BOUNDED: names such as "Sackpack"
    /// contain "pack", so an unbounded pattern would rescue a real bag.
    /// </summary>
    [GeneratedRegex(@"\b(paket|paketi|paketleri|set|seti|setleri|pack|kit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BundleMarkerRegex();

    /// <summary>
    /// Apparel and bag group: accessories the bundle exception does NOT extend to.
    /// The standing rule: "a bag is a bag even as a gift".
    /// The pattern is ASCII because the name arrives already folded to ASCII.
    /// </summary>
    [GeneratedRegex(@"\b(t-?shirt|tisort[a-z]*|sweatshirt|sweatpant[a-z]*|hoodie|kap[u]?son[a-z]*|kolsuz|jogger[a-z]*|tayt|legging[a-z]*|sapka[a-z]*|sort[a-z]*|korse[a-z]*|esofman[a-z]*|canta(si|lar|lari)?|handbag|atlet(i|ler|leri)?|maskot)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ApparelOrBagRegex();

    /// <summary>
    /// Does the name contain a real supplement ingredient? The bundle exception only
    /// works together with this; otherwise condiment bundles such as a food pack
    /// "(PANCAKE + CHOCOLATE SAUCE)" would be rescued too.
    /// </summary>
    [GeneratedRegex(@"\b(whey|protein|proteini|bcaa|eaa|kreatin|creatine|amino|vitamin|gainer|glutamin|glutamine|kolajen|collagen|karnitin|carnitine|arginin|arginine)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupplementMarkerRegex();

    /// <summary>
    /// Folds Turkish letters to their ASCII counterparts. The patterns are written
    /// in that alphabet, so the letter case traps end in one place.
    /// </summary>
    private static string FoldTurkishToAscii(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            buffer[i] = value[i] switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' or 'I' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                var c => c,
            };
        }

        return new string(buffer);
    }
}
