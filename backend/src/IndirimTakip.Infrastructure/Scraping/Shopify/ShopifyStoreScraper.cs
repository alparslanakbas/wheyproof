using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>
/// Collects a Shopify store's catalog from its public products.json endpoint.
/// </summary>
/// <remarks>
/// <b>One class, many stores.</b> The Turkish site had a scraper class per
/// brand, and most of them differed only in name and URL. On the US shortlist
/// almost every brand is on Shopify, so the difference is data
/// (<see cref="ShopifyStores"/>), not code.
///
/// <b>Prices must be USD.</b> Shopify localises by visitor country, and our
/// server is in Frankfurt: on 2026-09-11, 17 of 18 stores answered in USD but
/// Optimum Nutrition answered in EUR, which would have shown euro amounts
/// under a dollar sign without any error. Every request asks for USD, and each
/// crawl first checks the storefront's own currency marker; a store that still
/// is not USD is skipped rather than ingested.
///
/// <b>Each size is its own product.</b> A Shopify product holds every size and
/// flavor as variants with their own prices (Nutricost whey: 1.5 lb, 2 lb and
/// 5 lb on one page). Taking one variant would publish an arbitrary size's
/// price. Variants are grouped by their non-flavor options; flavors of one
/// size collapse into a single product priced at the cheapest in-stock one,
/// because an out-of-stock flavor cannot be bought.
///
/// <b>A 429 stops the crawl but keeps what was collected.</b> Dropping a
/// partial catalog would lose that day's price points for everything fetched.
/// </remarks>
public sealed partial class ShopifyStoreScraper(HttpClient httpClient, ShopifyStore store, ILogger<ShopifyStoreScraper> logger)
    : IBrandScraper
{
    public const string HttpClientName = "shopify";

    private const string UsdQuery = "currency=USD";
    private const int PageSize = 250;
    private const int MaxPages = 20;

    // Polite gap between catalog pages. One store (Ghost) answered 429 to a
    // handful of quick requests during the survey.
    private static readonly TimeSpan PageDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // Shopify product types that are never supplements (case-insensitive).
    // "ATHLETICS" is Ghost's clothing line; "pantry" is Nutricost's spice range.
    private static readonly HashSet<string> ExcludedProductTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "gift card", "gift cards", "apparel", "clothing", "merch", "merchandise", "accessories",
        "accessory", "gear", "shaker", "shakers", "hats", "headwear", "athletics", "pantry", "drinkware",
    };

    // Option names that describe flavor rather than a different package.
    private static readonly HashSet<string> FlavorOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "flavor", "flavour", "flavors", "flavours", "taste",
    };

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureUsdStorefrontAsync(cancellationToken);

        var result = new List<ScrapedProduct>();
        var seller = store.IsRetailer ? SellerFromBaseUrl(store.BaseUrl) : null;

        for (var page = 1; page <= MaxPages; page++)
        {
            ShopifyProductsResponse? response;
            try
            {
                response = await httpClient.GetFromJsonAsync<ShopifyProductsResponse>(
                    $"{store.BaseUrl}/products.json?limit={PageSize}&page={page}&{UsdQuery}", JsonOptions, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("{Store}: rate limited on page {Page}; keeping {Count} products.",
                    store.BrandName, page, result.Count);
                break;
            }

            if (response is null || response.Products.Count == 0)
                break;

            foreach (var product in response.Products)
                result.AddRange(ToScrapedProducts(product, store, seller));

            if (response.Products.Count < PageSize)
                break;

            await Task.Delay(PageDelay, cancellationToken);
        }

        return result;
    }

    private async Task EnsureUsdStorefrontAsync(CancellationToken cancellationToken)
    {
        string html;
        try
        {
            html = await httpClient.GetStringAsync($"{store.BaseUrl}?{UsdQuery}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Store}: storefront did not load; relying on the currency parameter.", store.BrandName);
            return;
        }

        var currency = ParseStorefrontCurrency(html);
        if (currency is null)
        {
            logger.LogWarning("{Store}: storefront does not declare a currency; relying on the currency parameter.", store.BrandName);
            return;
        }

        if (!currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{store.BrandName}: storefront currency is {currency}, not USD. Skipped so that prices in the wrong currency never reach the site.");
        }
    }

    /// <summary>The storefront's active currency from Shopify's page marker, or null.</summary>
    internal static string? ParseStorefrontCurrency(string html)
    {
        var match = StorefrontCurrencyRegex().Match(html);
        return match.Success ? match.Groups["code"].Value.ToUpperInvariant() : null;
    }

    internal static IEnumerable<ScrapedProduct> ToScrapedProducts(ShopifyProduct product, ShopifyStore store, string? seller)
    {
        if (IsExcluded(product))
            yield break;

        var title = product.Title.Trim();
        var brand = store.IsRetailer ? product.Vendor?.Trim() : null;

        // The store's product type ("PRE-WORKOUT", "Carb Powders") is a useful
        // second signal next to the name.
        var categoryText = string.IsNullOrWhiteSpace(product.ProductType) ? title : $"{title} {product.ProductType}";
        var category = ProductAttributeParser.InferCategory(categoryText, brand ?? store.BrandName);

        var sizePositions = product.Options
            .Where(o => !FlavorOptionNames.Contains(o.Name.Trim()))
            .Select(o => o.Position)
            .OrderBy(p => p)
            .ToList();

        // A zero price would divide by zero in the discount maths.
        var groups = product.Variants
            .Where(v => v.Price > 0)
            .GroupBy(v => SizeKey(v, sizePositions))
            .ToList();

        var hasSizeDimension = groups.Any(g => g.Key.Length > 0);

        foreach (var group in groups)
        {
            var inStock = group.Where(v => v.Available).ToList();
            var chosen = (inStock.Count > 0 ? inStock : group.ToList()).MinBy(v => v.Price)!;
            var label = group.Key;

            var name = label.Length == 0 || title.Contains(label, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title} - {label}";

            // A size-specific link lands the shopper on that size. Products with
            // no size dimension keep the plain URL, which also stays their
            // identity across crawls.
            var url = hasSizeDimension
                ? $"{store.BaseUrl}/products/{product.Handle}?variant={chosen.Id}"
                : $"{store.BaseUrl}/products/{product.Handle}";

            yield return new ScrapedProduct(
                Name: name,
                Url: url,
                ImageUrl: product.Images.Count > 0 ? product.Images[0].Src : null,
                Category: category,
                Price: chosen.Price,
                StoreOldPrice: chosen.CompareAtPrice > chosen.Price ? chosen.CompareAtPrice : null,
                BrandName: brand,
                InStock: inStock.Count > 0,
                Seller: seller,
                ServingsPerPackage: ProductAttributeParser.ExtractServings(label.Length > 0 ? label : title));
        }
    }

    private static bool IsExcluded(ShopifyProduct product)
    {
        var type = product.ProductType?.Trim();
        if (!string.IsNullOrEmpty(type) && ExcludedProductTypes.Contains(type))
            return true;

        return ApparelOrMerchRegex().IsMatch(product.Title)
            || NonSupplementProductFilter.IsAccessoryOrApparel(product.Title);
    }

    private static string SizeKey(ShopifyVariant variant, List<int> positions)
    {
        var values = positions
            .Select(p => p switch { 1 => variant.Option1, 2 => variant.Option2, 3 => variant.Option3, _ => null })
            .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Equals("Default Title", StringComparison.OrdinalIgnoreCase))
            .Select(v => v!.Trim());

        return string.Join(" / ", values);
    }

    private static string SellerFromBaseUrl(string baseUrl)
    {
        var host = new Uri(baseUrl).Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    [GeneratedRegex(@"Shopify\.currency\s*=\s*\{\s*""active""\s*:\s*""(?<code>[A-Za-z]{3})""")]
    private static partial Regex StorefrontCurrencyRegex();

    // English apparel and merch words, as whole words. "caps" is deliberately
    // absent: supplement titles use it for capsules ("Ashwagandha 120 Caps").
    [GeneratedRegex(@"\b(t-?shirts?|tees?|tank\s*tops?|tanks?|hoodies?|crewnecks?|sweatshirts?|sweatpants|joggers?|shorts|socks|hats?|beanies?|snapbacks?|baseball\s*caps?|jackets?|leggings|sports?\s*bras?|apparel|towels?|backpacks?|duffels?|gym\s*bags?|totes?|stickers?|posters?|keychains?|gift\s*cards?|shakers?|blender\s*bottles?|water\s*bottles?|jugs?|pill\s*(cases?|organizers?)|funnels?|lanyards?|pantry|drinkware)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ApparelOrMerchRegex();
}
