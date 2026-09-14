using System.Text.RegularExpressions;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Picks the nutrition label image out of a product's image list by FILE NAME.
/// </summary>
/// <remarks>
/// <b>Only a name that says "label" counts; nothing is guessed.</b> Measured on
/// 2026-09-14 (20 protein products per store): Nutricost names it "..._SFP_...",
/// Orgain "..._NFP_...", Naked "...-nutrition-facts...", Promix
/// "...-supplement-facts...", BulkSupplements "...-Label-V006...". Picking "the
/// third photo" instead would feed product shots to the reader, and a product
/// with no named label simply keeps an empty nutrition table.
///
/// <b>Two traps from the same survey.</b> Quest's product photos carry the brand
/// name ("quest-nutrition-...") so "nutrition" alone is not a label marker, and
/// Transparent Labs names its FRONT label "TL-Label-..._FRONT_...", which shows
/// the tub, not the facts panel.
/// </remarks>
internal static partial class NutritionLabelImagePicker
{
    public static string? Pick(IEnumerable<string> imageUrls) =>
        imageUrls.FirstOrDefault(IsLabelImage);

    internal static bool IsLabelImage(string url)
    {
        var fileName = FileName(url);
        return LabelMarkerRegex().IsMatch(fileName) && !FrontRegex().IsMatch(fileName);
    }

    /// <summary>
    /// The URL the reader fetches: Shopify's CDN resizes on request, and a
    /// 1200 px image keeps small print legible without sending a 4000 px file.
    /// </summary>
    public static string ForReading(string url, int width)
    {
        var withoutQuery = url.Split('?', 2)[0];
        return withoutQuery.Contains("cdn.shopify.com", StringComparison.OrdinalIgnoreCase)
            ? $"{withoutQuery}?width={width}"
            : url;
    }

    private static string FileName(string url)
    {
        var path = url.Split('?', 2)[0];
        var name = path[(path.LastIndexOf('/') + 1)..];
        return Uri.UnescapeDataString(name).ToLowerInvariant();
    }

    [GeneratedRegex(@"(^|[^a-z])(sfp|nfp|facts?|label)([^a-z]|$)|nutrition[-_ ]?(facts?|label|panel|info)|supplement[-_ ]?(facts?|label|panel)")]
    private static partial Regex LabelMarkerRegex();

    [GeneratedRegex(@"(^|[^a-z])front([^a-z]|$)")]
    private static partial Regex FrontRegex();
}
