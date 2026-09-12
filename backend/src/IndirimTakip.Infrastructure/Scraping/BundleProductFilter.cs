using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Recognizes sets selling SEVERAL PRODUCTS in one box.
///
/// They aren't non-supplements (the contents are real products).
/// NOTE: THIS IS NOT A GENERAL POLICY. The site's established behavior is to KEEP
/// bundles: measured on the Turkish site, 102 bundle products across nine brands
/// were live, and none had serving data, so they didn't distort the price per
/// serving either.
///
/// The filter is used only for sources whose bundles are COPIES OF THE SAME THING.
/// Before adding it to a new source, CHECK whether that source's bundles are really
/// separate products or copies.
///
/// The pattern matches Turkish words ("paketi", "seti": "bundle", "set" with the
/// possessive suffix), because it was written for a Turkish source; no US store
/// uses it. It is kept NARROW: only the words themselves.
/// </summary>
public static partial class BundleProductFilter
{
    /// <summary>
    /// Does the product name describe a multi-product set?
    ///
    /// Letter case trap: the dotted İ in "PAKETİ" doesn't fold to "i" with
    /// OrdinalIgnoreCase, so the dotted/dotless distinction is removed first.
    /// </summary>
    public static bool IsBundle(string name)
    {
        var normalized = name.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
        return BundleWordRegex().IsMatch(normalized);
    }

    /// <summary>
    /// THE POSSESSIVE SUFFIX IS REQUIRED; bare "paket" is NOT searched. A catalog
    /// item read "... 22.4 gram Tek Paket Servis" ("single packet serving"): a
    /// single-serving product, not a set. Searching "paket" alone would drop it.
    ///
    /// The word boundary is required too: a plain substring search would hit inside
    /// words such as "korseti", "reset" or "preset".
    /// </summary>
    [GeneratedRegex(@"\b(paketi|seti)\b")]
    private static partial Regex BundleWordRegex();
}
