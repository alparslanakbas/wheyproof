using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>
/// Collects a Shopify store's catalog from its public products.json endpoint.
/// </summary>
/// <remarks>
/// <b>One class, many stores.</b> The Turkish site had a scraper class per
/// brand, and most of them differed only in name and URL. On the US
/// shortlist almost every brand is on Shopify, so the difference is data
/// (see <see cref="ShopifyStores"/>), not code.
///
/// <b>A 429 stops the crawl but keeps what was collected.</b> Discarding a
/// partial catalog on a rate limit would drop that day's price points for
/// everything already fetched, which is worse than a short list.
/// </remarks>
public sealed class ShopifyStoreScraper(HttpClient httpClient, ShopifyStore store, ILogger<ShopifyStoreScraper> logger)
    : IBrandScraper
{
    public const string HttpClientName = "shopify";

    private const int PageSize = 250;
    private const int MaxPages = 20;

    // Polite gap between catalog pages. One store (Ghost) answered 429 to a
    // handful of quick requests during the survey.
    private static readonly TimeSpan PageDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // Product types that are never supplements, matched case-insensitively.
    // The name-based accessory filter still runs afterwards, for stores that
    // leave the type empty.
    private static readonly string[] ExcludedProductTypes =
    [
        "gift card", "gift cards", "apparel", "clothing", "merch", "merchandise",
        "accessories", "accessory", "gear", "shaker", "shakers", "bottle", "bottles",
    ];

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ScrapedProduct>();
        var seller = store.IsRetailer ? new Uri(store.BaseUrl).Host.Replace("www.", "") : null;

        for (var page = 1; page <= MaxPages; page++)
        {
            ShopifyProductsResponse? response;
            try
            {
                response = await httpClient.GetFromJsonAsync<ShopifyProductsResponse>(
                    $"{store.BaseUrl}/products.json?limit={PageSize}&page={page}", JsonOptions, cancellationToken);
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
            {
                var item = ToScrapedProduct(product, store, seller);
                if (item is not null)
                    result.Add(item);
            }

            if (response.Products.Count < PageSize)
                break;

            await Task.Delay(PageDelay, cancellationToken);
        }

        return result;
    }

    internal static ScrapedProduct? ToScrapedProduct(ShopifyProduct product, ShopifyStore store, string? seller)
    {
        var type = product.ProductType?.Trim();
        if (!string.IsNullOrEmpty(type)
            && ExcludedProductTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (product.Title.Contains("gift card", StringComparison.OrdinalIgnoreCase)
            || NonSupplementProductFilter.IsAccessoryOrApparel(product.Title))
        {
            return null;
        }

        // Prefer an in-stock variant, otherwise the first one: an out-of-stock
        // product has to stay in the crawl or its price history gets gaps.
        var variant = product.Variants.Find(v => v.Available) ?? product.Variants.FirstOrDefault();

        // A zero price would divide by zero in the discount maths.
        if (variant is null || variant.Price <= 0)
            return null;

        return new ScrapedProduct(
            Name: product.Title.Trim(),
            Url: $"{store.BaseUrl}/products/{product.Handle}",
            ImageUrl: product.Images.Count > 0 ? product.Images[0].Src : null,
            // Store product types are not our category slugs; the category is
            // inferred from the name during ingestion.
            Category: null,
            Price: variant.Price,
            StoreOldPrice: variant.CompareAtPrice > variant.Price ? variant.CompareAtPrice : null,
            BrandName: store.IsRetailer ? product.Vendor?.Trim() : null,
            InStock: product.Variants.Any(v => v.Available),
            Seller: seller);
    }
}
