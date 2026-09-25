using IndirimTakip.Core.Scraping;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// The shared check every source's products pass before ingestion.
/// </summary>
/// <remarks>
/// <b>WHY (security/architecture review, 2026-09-26).</b> These guards were
/// written per scraper and drifted: the shared Shopify scraper, which covers
/// most US and UK stores, had no zero-price guard at all (a zero price once
/// caused a division by zero in the Turkish site's discount math). Same path as
/// moving the brand alias map into <c>ResolveBrand</c>: the rule lives in the
/// one gate everything passes, not in each scraper.
///
/// <b>IT CHANGES NOTHING TODAY (measured).</b> In both live databases (US and
/// UK) non-https product or image URLs: 0, zero/negative prices: 0, categories
/// outside the list: 0. The check is against a source sending bad data later.
///
/// A product whose URL isn't https is dropped: the site links to it and the
/// server requests it. A non-https image only loses the image (a browser won't
/// load an http image on an https page anyway). A category that isn't one of
/// the site's slugs is cleared and name-based inference takes over: a raw
/// source label means a product that appears on no category page.
/// </remarks>
internal static class ScrapedProductGuard
{
    /// <summary>The cleaned record; null when it isn't taken.</summary>
    public static ScrapedProduct? Clean(ScrapedProduct product)
    {
        if (product.Price <= 0 || !IsHttps(product.Url))
            return null;

        var image = product.ImageUrl is not null && IsHttps(product.ImageUrl) ? product.ImageUrl : null;
        var category = product.Category is not null && ProductAttributeParser.CategorySlugs.Contains(product.Category)
            ? product.Category
            : null;

        return image == product.ImageUrl && category == product.Category
            ? product
            : product with { ImageUrl = image, Category = category };
    }

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
