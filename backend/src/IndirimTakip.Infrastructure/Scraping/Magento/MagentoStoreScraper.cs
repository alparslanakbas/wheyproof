using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Magento;

/// <summary>A Magento store we collect prices from.</summary>
/// <param name="SitemapUrl">The store's product sitemap for this market.</param>
/// <param name="ProductPathMarker">
/// The path segment in front of a product's url_key in that sitemap
/// ("/uk/products/" on bulk.com); entries without it aren't products.
/// </param>
public sealed record MagentoStore(
    string BrandName, string BaseUrl, string SitemapUrl, string ProductPathMarker,
    IReadOnlySet<string>? OnlyCategories = null, SiteMarket? Market = null)
{
    public SiteMarket StoreMarket => Market ?? SiteMarket.Us;
}

public static class MagentoStores
{
    public static readonly IReadOnlyList<MagentoStore> All =
    [
        // Bulk (UK). Its root sends our Frankfurt server to /de/, but the /uk/
        // pages answer in GBP, and the storefront GraphQL defaults to the UK
        // store in GBP. Measured 2026-09-19: on three products the GraphQL
        // variants matched the product pages' offers SKU for SKU (93/93, 16/16,
        // 2/2) with no price difference. Its product sitemap lists 244 products.
        // Limited to the sport categories for the BulkSupplements reason: a broad
        // own-label range of extracts and vitamins nothing else compares with.
        new("Bulk", "https://www.bulk.com", "https://www.bulk.com/media/feeds/sitemapUK.xml", "/uk/products/",
            OnlyCategories: ShopifyStores.SportCategories, Market: SiteMarket.Uk),
    ];

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<MagentoStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}

/// <summary>
/// Reads a Magento store through its public storefront GraphQL API: product
/// addresses from the store's sitemap, then the catalog in batches of 50 by
/// url_key. No request per product.
/// </summary>
/// <remarks>
/// <b>Sizes come from the variants' own labels.</b> Bulk's page offers carry
/// no names, and the size code at the end of a SKU means grams on one product
/// ("-0450" is 450g) and a capsule count on another ("-0090" is 90 capsules),
/// so reading it would invent sizes. GraphQL labels each variant ("2.5kg",
/// "90 Capsules"). As with Shopify, each size is its own product and its
/// flavours collapse into the cheapest one in stock.
///
/// <b>Each size gets its own address:</b> the product page with Magento's
/// option parameter (<c>?o=</c>, base64 of "attributeId-valueId"), the form
/// the store uses in its own links, which opens the page on that size.
/// </remarks>
public sealed partial class MagentoStoreScraper(
    HttpClient httpClient, MagentoStore store, ILogger<MagentoStoreScraper> logger) : IBrandScraper
{
    public const string HttpClientName = "magento-store";

    private const int BatchSize = 50;
    private const decimal MinimumPrice = 1m;
    private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new();

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    private const string Query = """
        query ($keys: [String]) {
          products(filter: { url_key: { in: $keys } }, pageSize: 50) {
            items {
              name url_key stock_status
              small_image { url }
              price_range { minimum_price { final_price { value currency } regular_price { value } } }
              ... on ConfigurableProduct {
                configurable_options { attribute_code attribute_id }
                variants {
                  attributes { code label value_index }
                  product {
                    stock_status
                    price_range { minimum_price { final_price { value currency } regular_price { value } } }
                  }
                }
              }
            }
          }
        }
        """;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var sitemap = await httpClient.GetStringAsync(store.SitemapUrl, cancellationToken);
        var productUrls = ProductUrlsByKey(sitemap, store.ProductPathMarker);
        var result = new List<ScrapedProduct>();

        foreach (var batch in productUrls.Keys.Chunk(BatchSize))
        {
            List<MagentoProduct> items;
            try
            {
                items = await FetchAsync(batch, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("{Store}: rate limited; keeping {Count} products.", store.BrandName, result.Count);
                break;
            }

            foreach (var product in items)
            {
                if (productUrls.TryGetValue(product.UrlKey, out var url))
                    result.AddRange(ToScrapedProducts(product, url, store));
            }

            await Task.Delay(BatchDelay, cancellationToken);
        }

        return result;
    }

    private async Task<List<MagentoProduct>> FetchAsync(string[] keys, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"{store.BaseUrl}/graphql", new { query = Query, variables = new { keys } }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<MagentoResponse>(JsonOptions, cancellationToken);
        if (body?.Errors is { Count: > 0 } errors)
            throw new InvalidOperationException($"{store.BrandName}: GraphQL error: {errors[0].Message}");

        return body?.Data?.Products?.Items ?? [];
    }

    /// <summary>url_key -> product page address, from the store's sitemap.</summary>
    internal static Dictionary<string, string> ProductUrlsByKey(string sitemap, string productPathMarker)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in LocRegex().Matches(sitemap))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
            var at = url.IndexOf(productPathMarker, StringComparison.Ordinal);
            if (at < 0)
                continue;

            var key = url[(at + productPathMarker.Length)..].Split('/', 2)[0];
            if (key.Length > 0)
                result.TryAdd(key, url);
        }

        return result;
    }

    internal static IEnumerable<ScrapedProduct> ToScrapedProducts(MagentoProduct product, string productUrl, MagentoStore store)
    {
        var name = CleanName(product.Name);
        if (name.Length == 0 || NonSupplementProductFilter.IsAccessoryOrApparel(name))
            yield break;

        var category = ProductAttributeParser.InferCategory(name, store.BrandName)
            ?? ProductAttributeParser.InferCategory(name);
        if (store.OnlyCategories is { } allowed && (category is null || !allowed.Contains(category)))
            yield break;

        var image = product.SmallImage?.Url;
        var sizeAttributes = (product.ConfigurableOptions ?? [])
            .Where(o => !IsFlavourAttribute(o.AttributeCode))
            .ToDictionary(o => o.AttributeCode, o => o.AttributeId, StringComparer.Ordinal);

        if (product.Variants is not { Count: > 0 } variants)
        {
            // A simple product: one price, no sizes to split.
            if (Offer(product.PriceRange, store) is { } simple)
                yield return Row(name, productUrl, image, category, simple, IsInStock(product.StockStatus));
            yield break;
        }

        var bySize = variants
            .Where(v => v.Product is not null)
            .GroupBy(v => SizeKey(v, sizeAttributes))
            .OrderBy(g => g.Key.Label, StringComparer.Ordinal)
            .ToList();

        // A name can already carry one of its own sizes ("Caffeine 200mg 100
        // Tablets" sells 100 and 250 tablets); appending the other size to it
        // gave "... 100 Tablets - 250 Tablets". Take the sizes out first.
        var baseName = WithoutSizeLabels(name, bySize.Select(g => g.Key.Label));

        foreach (var size in bySize)
        {
            // Cheapest in-stock flavour; out of stock everywhere -> cheapest, marked out of stock.
            var offers = size
                .Select(v => (Offer: Offer(v.Product!.PriceRange, store), InStock: IsInStock(v.Product.StockStatus)))
                .Where(o => o.Offer is not null)
                .ToList();
            if (offers.Count == 0)
                continue;

            var inStock = offers.Where(o => o.InStock).ToList();
            var best = (inStock.Count > 0 ? inStock : offers).MinBy(o => o.Offer!.Value.Price);

            var fullName = size.Key.Label.Length == 0 ? name : $"{baseName} - {size.Key.Label}";
            var url = size.Key.OptionParam is null ? productUrl : $"{productUrl}?o={size.Key.OptionParam}";

            yield return Row(fullName, url, image, category, best.Offer!.Value, inStock.Count > 0);
        }
    }

    private static ScrapedProduct Row(string name, string url, string? image, string? category,
        (decimal Price, decimal? OldPrice) offer, bool inStock) =>
        new(
            Name: name,
            Url: url,
            ImageUrl: image,
            Category: category,
            Price: offer.Price,
            StoreOldPrice: offer.OldPrice,
            InStock: inStock,
            ServingsPerPackage: ProductAttributeParser.ExtractServings(name));

    /// <summary>
    /// A variant's price, or null below the price floor. The currency must be the
    /// store's market currency; anything else is refused loudly, because GBP shown
    /// as dollars (or the reverse) looks plausible and is wrong.
    /// </summary>
    private static (decimal Price, decimal? OldPrice)? Offer(MagentoPriceRange? range, MagentoStore store)
    {
        var final = range?.MinimumPrice?.FinalPrice;
        if (final?.Value is not { } price)
            return null;

        if (!string.Equals(final.Currency, store.StoreMarket.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{store.BrandName}: the store answered in {final.Currency ?? "an unknown currency"}, " +
                $"{store.StoreMarket.Currency} expected.");
        }

        if (price < MinimumPrice)
            return null;

        var regular = range!.MinimumPrice!.RegularPrice?.Value;
        return (price, regular > price ? regular : null);
    }

    private static (string Label, string? OptionParam) SizeKey(MagentoVariant variant, Dictionary<string, string?> sizeAttributes)
    {
        var parts = variant.Attributes.Where(a => sizeAttributes.ContainsKey(a.Code)).ToList();
        if (parts.Count == 0)
            return ("", null);

        var label = string.Join(" ", parts.Select(a => a.Label.Trim()));
        var ids = parts
            .Where(a => sizeAttributes[a.Code] is not null)
            .Select(a => $"{sizeAttributes[a.Code]}-{a.ValueIndex}")
            .ToList();
        var param = ids.Count == parts.Count
            ? Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Join(",", ids)))
            : null;
        return (label, param);
    }

    internal static string WithoutSizeLabels(string name, IEnumerable<string> labels)
    {
        var result = name;
        foreach (var label in labels.Where(l => l.Length > 0))
            result = Regex.Replace(result, $@"\s*[-–,]?\s*\b{Regex.Escape(label)}\b", "", RegexOptions.IgnoreCase);

        result = result.Trim().TrimEnd('-', '–', ',').Trim();
        return result.Length > 0 ? result : name;
    }

    // "bp_flavour" on Bulk; the spelling varies between stores.
    internal static bool IsFlavourAttribute(string code) =>
        code.Contains("flavour", StringComparison.OrdinalIgnoreCase)
        || code.Contains("flavor", StringComparison.OrdinalIgnoreCase);

    private static bool IsInStock(string? status) =>
        string.Equals(status, "IN_STOCK", StringComparison.OrdinalIgnoreCase);

    // Store names carry trademark signs ("Pure Whey Protein™"): noise in a
    // product name, and they break matching against the same product elsewhere.
    private static string CleanName(string name) =>
        TrademarkRegex().Replace(WebUtility.HtmlDecode(name), "").Trim();

    [GeneratedRegex(@"<loc>(?<url>[^<]+)</loc>")]
    private static partial Regex LocRegex();

    [GeneratedRegex("[™®©]")]
    private static partial Regex TrademarkRegex();
}
