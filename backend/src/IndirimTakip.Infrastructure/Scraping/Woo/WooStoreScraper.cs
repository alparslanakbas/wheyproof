using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.NutritionLabels;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Woo;

/// <summary>A WooCommerce store we collect prices from.</summary>
/// <param name="BrandName">Canonical brand name for a single-brand store.</param>
/// <param name="BaseUrl">
/// Storefront origin INCLUDING the market path when the store has one, and no
/// trailing slash: Form's root serves the UK catalog in GBP to our server,
/// "/us" serves the US catalog in USD (measured from the production server,
/// 2026-09-18). The path is part of the store's identity here, not a detail.
/// </param>
/// <param name="OnlyCategories">
/// When set, only products in these categories are kept; uncategorised ones go
/// too. For stores whose catalog reaches past what we compare.
/// </param>
public sealed record WooStore(string BrandName, string BaseUrl, IReadOnlySet<string>? OnlyCategories = null);

/// <summary>
/// Reads a WooCommerce store's public Store API (/wp-json/wc/store/v1/products).
/// One request per 100 products, no request per product: every product in the
/// first store on this path publishes a single package size, and the variations
/// differ only by flavor (measured 2026-09-18). If a store ever ships two sizes
/// under one product, they share this row's price, so the size list is checked
/// and the product is logged rather than silently priced from one variant.
/// </summary>
public sealed class WooStoreScraper(
    HttpClient httpClient, WooStore store, ILogger<WooStoreScraper> logger) : IBrandScraper
{
    public const string HttpClientName = "woo-store";

    private const int PageSize = 100;
    private const int MaxPages = 20;
    private const decimal MinimumPrice = 1m;

    // The currency the site must answer in. A store that switches us to its home
    // market would otherwise publish, say, GBP amounts as dollars: the prices
    // would look plausible and be wrong by the exchange rate.
    private const string RequiredCurrency = "USD";

    private static readonly JsonSerializerOptions JsonOptions = new();

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ScrapedProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{store.BaseUrl}/wp-json/wc/store/v1/products?per_page={PageSize}&page={page}";
            var products = await httpClient.GetFromJsonAsync<List<WooProduct>>(url, JsonOptions, cancellationToken);

            if (products is null || products.Count == 0)
                break;

            foreach (var product in products)
                result.AddRange(ToScrapedProducts(product, store, logger));

            if (products.Count < PageSize)
                break;
        }

        logger.LogInformation("{Brand}: {Count} products scraped.", store.BrandName, result.Count);
        return result;
    }

    internal static IEnumerable<ScrapedProduct> ToScrapedProducts(
        WooProduct product, WooStore store, ILogger? logger = null)
    {
        var name = WebUtility.HtmlDecode(product.Name).Replace('–', '-').Trim();

        // A bundle builder ("Build Your Bundle") has no price of its own, and a
        // store's leftover "Test product" is not for sale. Both were live in the
        // first store's catalog (measured 2026-09-18).
        if (name.Length == 0
            || IsNonProduct(name)
            || !product.IsPurchasable
            || string.Equals(product.Type, "bundle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(product.Type, "grouped", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (NonSupplementProductFilter.IsAccessoryOrApparel(name) || IsExcludedCategory(product))
            yield break;

        var prices = product.Prices;
        if (!string.Equals(prices?.CurrencyCode, RequiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            // Loud, not silent: a wrong currency is the failure that looks right.
            throw new InvalidOperationException(
                $"{store.BrandName}: the store answered in {prices?.CurrencyCode ?? "an unknown currency"}, " +
                $"{RequiredCurrency} expected. Check the storefront path in WooStores.");
        }

        var price = ToMajorUnits(prices!.Price, prices.CurrencyMinorUnit);
        if (price is null || price < MinimumPrice)
            yield break;

        var regular = ToMajorUnits(prices.RegularPrice, prices.CurrencyMinorUnit);
        var categoryText = string.Join(' ', product.Categories.Select(c => c.Name).Prepend(name));
        var category = ProductAttributeParser.InferCategory(categoryText, store.BrandName)
            ?? ProductAttributeParser.InferCategory(categoryText);

        if (store.OnlyCategories is { } allowed && (category is null || !allowed.Contains(category)))
            yield break;

        var sizes = SizeLabels(product);
        if (sizes.Count > 1)
        {
            // One row cannot carry two package prices. Reported instead of
            // guessing, so the store's change is visible in the log.
            logger?.LogWarning(
                "{Brand}: \"{Product}\" publishes {Count} sizes ({Sizes}) under one product; listed with the catalog price.",
                store.BrandName, name, sizes.Count, string.Join(", ", sizes));
        }

        var size = sizes.Count == 1 ? sizes[0] : null;
        var fullName = size is null || name.Contains(size, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} - {size}";

        yield return new ScrapedProduct(
            Name: fullName,
            Url: product.Permalink,
            ImageUrl: product.Images.Count > 0 ? product.Images[0].Src : null,
            Category: category,
            Price: price.Value,
            StoreOldPrice: regular > price ? regular : null,
            InStock: product.IsInStock,
            ServingsPerPackage: ProductAttributeParser.ExtractServings(fullName),
            NutritionLabelImageUrl: NutritionLabelImagePicker.Pick(product.Images.Select(i => i.Src)));
    }

    /// <summary>Minor units to a real price: "3900" with 2 decimals is 39.00.</summary>
    internal static decimal? ToMajorUnits(string? amount, int minorUnit)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;

        for (var i = 0; i < minorUnit; i++)
            value /= 10m;

        return value;
    }

    /// <summary>
    /// The sizes this product actually sells, read from the VARIATIONS. The
    /// attribute's own term list is the site-wide taxonomy: Form's protein lists
    /// three sizes there while selling one (measured 2026-09-18), so reading it
    /// would invent packages the store does not offer.
    /// </summary>
    internal static List<string> SizeLabels(WooProduct product)
    {
        var display = product.Attributes
            .Where(a => IsSizeAttribute(a.Name))
            .SelectMany(a => a.Terms)
            .Where(t => !string.IsNullOrWhiteSpace(t.Slug))
            .GroupBy(t => t.Slug!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Name), StringComparer.OrdinalIgnoreCase);

        return product.Variations
            .SelectMany(v => v.Attributes)
            .Where(a => IsSizeAttribute(a.Name) && !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => display.TryGetValue(a.Value!, out var label) ? label : a.Value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSizeAttribute(string name) =>
        name.Trim().StartsWith("size", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonProduct(string name) =>
        name.Contains("test product", StringComparison.OrdinalIgnoreCase)
        || name.Contains("build your bundle", StringComparison.OrdinalIgnoreCase)
        || name.Contains("gift card", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedCategory(WooProduct product) =>
        product.Categories.Any(c =>
            c.Name.Contains("accessor", StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains("apparel", StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains("merch", StringComparison.OrdinalIgnoreCase));
}
