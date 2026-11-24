using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.BigCommerce;

/// <summary>A BigCommerce store we collect prices from.</summary>
/// <param name="Name">The source's name; a retailer's products carry their own brands.</param>
/// <param name="BaseUrl">Storefront origin, no trailing slash.</param>
/// <param name="OnlyCategories">
/// When set, only products in these categories are kept; uncategorised ones go too.
/// </param>
/// <param name="Market">The edition the store belongs to; null means US.</param>
/// <param name="OwnLabelPrefixes">
/// Brand values that are the retailer's own label, not a maker: A1 files some
/// products under "A1 Supplements Store" or "A1 Plus" although their names say
/// "GAT Sport CLA 1250" and "Ayone Nutrition Creatine". Such a product is skipped
/// rather than listed under a brand that doesn't make it.
/// </param>
public sealed record BigCommerceStore(
    string Name,
    string BaseUrl,
    IReadOnlySet<string>? OnlyCategories = null,
    SiteMarket? Market = null,
    IReadOnlyList<string>? OwnLabelPrefixes = null)
{
    public SiteMarket StoreMarket => Market ?? SiteMarket.Us;
}

public static class BigCommerceStores
{
    /// <summary>
    /// BigCommerce stores, measured from the production server on 2026-11-24.
    /// </summary>
    public static readonly IReadOnlyList<BigCommerceStore> All =
    [
        // A1 Supplements, a Miami sports nutrition distributor (Awin 58273): 245
        // products in its sitemap, robots.txt open to us, pages 200 from the server.
        // Most are out of stock, which is kept on purpose (prices are tracked through
        // restocks, owner's decision). Most of the catalog is NOW vitamins and herbs,
        // so only the sport categories are kept, for the BulkSupplements reason:
        // nothing else in our catalog compares against them. A full dry run on
        // 2026-11-24 kept 35 records; 173 pages fell outside the sport categories, 18
        // had a price range, 11 were accessories. The bare host is the store's own:
        // www answers 301 to it, and the sitemap lists bare addresses.
        new("A1 Supplements", "https://a1supplements.com", OnlyCategories: ShopifyStores.SportCategories,
            OwnLabelPrefixes: ["A1 "]),
    ];

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<BigCommerceStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}

/// <summary>
/// Reads a BigCommerce (Stencil) store from its product pages.
/// </summary>
/// <remarks>
/// <b>Addresses</b> come from the store's product sitemap
/// (<c>/xmlsitemap.php?type=products&amp;page=N</c>). Category listings can't be used:
/// the first store renders them in the browser, so the HTML carries no products.
///
/// <b>Data</b> comes from two places on every product page: the schema.org Product
/// (name, brand, address, image) and the storefront's own <c>BCData.product_attributes</c>
/// (the price shown, the non-sale and retail prices, stock). The price is the
/// storefront's; the schema.org offer is the fallback.
///
/// <b>One size per record.</b> A product whose sizes have different prices shows a
/// range (29.95 - 79.95) and the page doesn't say which size costs what; such a
/// product is skipped and counted, never priced from one end of the range (2 of 41
/// sampled pages). Flavours share a price, so a single size across the options is
/// written into the name ("- 30 Servings").
///
/// <b>Expired stock</b> ("60 Softgels Exp. 2/2024", seen on the first store) is a
/// clearance price for a short-dated product and isn't comparable; it's skipped.
///
/// <b>Once a day.</b> One page per product (about 220 kB unpacked); every six hours
/// would be heavy on their server for prices that change daily at most.
/// </remarks>
/// <param name="requestDelay">Pause between requests; tests pass zero.</param>
public sealed partial class BigCommerceStoreScraper(
    HttpClient httpClient, BigCommerceStore store, ILogger<BigCommerceStoreScraper> logger, TimeSpan? requestDelay = null)
    : IBrandScraper
{
    public const string HttpClientName = "bigcommerce-store";

    private const int MaxSitemapPages = 20;
    private const decimal MinimumPrice = 1m;

    private readonly TimeSpan delay = requestDelay ?? TimeSpan.FromSeconds(2);

    public string BrandName => store.Name;
    public string BaseUrl => store.BaseUrl;
    public bool DailyOnly => true;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var addresses = await ProductAddressesAsync(cancellationToken);
        if (addresses.Count == 0)
            throw new InvalidOperationException($"{store.Name}: the product sitemap listed no products; the store may have changed.");

        var seller = SellerFromBaseUrl(store.BaseUrl);
        var result = new List<ScrapedProduct>();
        var skipped = new Dictionary<SkipReason, int>();
        var failed = 0;

        foreach (var address in addresses)
        {
            await Task.Delay(delay, cancellationToken);

            string html;
            try
            {
                html = await httpClient.GetStringAsync(address, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                failed++;
                logger.LogWarning(ex, "{Store}: product page skipped: {Url}", store.Name, address);
                continue;
            }

            var page = ParseProductPage(html, address);
            if (page is null)
            {
                Count(skipped, SkipReason.NoProductData);
                continue;
            }

            // Loud, not silent: a wrong currency is the failure that looks right.
            if (!string.Equals(page.Currency, store.StoreMarket.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{store.Name}: the store answered in {page.Currency ?? "an unknown currency"}, " +
                    $"{store.StoreMarket.Currency} expected ({address}).");
            }

            var outcome = ToScrapedProduct(page, store, seller);
            if (outcome.Product is { } product)
                result.Add(product);
            else if (outcome.Reason is { } reason)
                Count(skipped, reason);
        }

        // Half the pages failing is not a few removed products: the run is wrong and
        // must not pass as a smaller catalog.
        if (failed * 2 > addresses.Count)
            throw new InvalidOperationException($"{store.Name}: {failed} of {addresses.Count} product pages failed.");

        logger.LogInformation(
            "{Store}: {Records} records from {Pages} product pages ({Failed} failed; skipped: {Skipped}).",
            store.Name, result.Count, addresses.Count, failed,
            skipped.Count == 0 ? "none" : string.Join(", ", skipped.Select(s => $"{s.Key} {s.Value}")));
        return result;
    }

    private async Task<List<string>> ProductAddressesAsync(CancellationToken cancellationToken)
    {
        var addresses = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; page <= MaxSitemapPages; page++)
        {
            using var response = await httpClient.GetAsync(
                $"{store.BaseUrl}/xmlsitemap.php?type=products&page={page}", cancellationToken);

            // The page after the last one is a 404, not an empty list (measured on
            // 2026-11-24: 245 products, page 2 answered 404 and failed the first run).
            // A 404 on the first page is a real failure.
            if (response.StatusCode == HttpStatusCode.NotFound && page > 1)
                break;

            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var added = 0;
            foreach (Match match in SitemapLocRegex().Matches(xml))
            {
                var address = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (seen.Add(address))
                {
                    addresses.Add(address);
                    added++;
                }
            }

            if (added == 0)
                break;

            await Task.Delay(delay, cancellationToken);
        }

        return addresses;
    }

    internal enum SkipReason
    {
        NoProductData,
        NoPrice,
        NoBrand,
        OwnLabel,
        PriceRange,
        Expired,
        Accessory,
        Category,
    }

    /// <summary>A record, or the reason there is none.</summary>
    internal sealed record Outcome(ScrapedProduct? Product, SkipReason? Reason)
    {
        public static Outcome Skip(SkipReason reason) => new(null, reason);
    }

    /// <summary>What one product page says.</summary>
    internal sealed record ProductPage(
        string Name,
        string? Brand,
        string Url,
        decimal? Price,
        decimal? RegularPrice,
        string? Currency,
        bool? InStock,
        string? ImageUrl,
        bool HasPriceRange,
        IReadOnlyList<string> OptionLabels);

    /// <summary>Reads a product page; null when it carries no schema.org Product.</summary>
    internal static ProductPage? ParseProductPage(string html, string requestedUrl)
    {
        if (FindProduct(html) is not { } product)
            return null;

        var name = Clean(StringOf(product, "name"));
        if (name.Length == 0)
            return null;

        var offer = FirstOffer(product);
        decimal? price = null, regular = null, minPrice = null, maxPrice = null;
        string? currency = null;
        bool? inStock = null;

        if (offer is { } o)
        {
            price = DecimalOf(o, "price");
            currency = StringOf(o, "priceCurrency");
            minPrice = DecimalOf(o, "minPrice");
            maxPrice = DecimalOf(o, "maxPrice");
            var availability = StringOf(o, "availability");
            if (availability.EndsWith("InStock", StringComparison.OrdinalIgnoreCase))
                inStock = true;
            else if (availability.EndsWith("OutOfStock", StringComparison.OrdinalIgnoreCase))
                inStock = false;
        }

        var hasRange = minPrice is not null && maxPrice is not null && minPrice != maxPrice;

        // The storefront's own object decides the price shown, the old prices and stock.
        if (ReadAttributes(html) is { } attributes)
        {
            if (attributes.Price is { } shown)
            {
                price = shown;
                currency = attributes.Currency ?? currency;
            }

            regular = attributes.NonSalePrice ?? attributes.RetailPrice;
            inStock = attributes.InStock ?? inStock;
            hasRange |= attributes.HasRange;
        }

        var url = StringOf(product, "url");
        return new ProductPage(
            Name: name,
            Brand: BrandOf(product, html),
            Url: url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : requestedUrl,
            Price: price,
            RegularPrice: regular,
            Currency: currency,
            InStock: inStock,
            ImageUrl: ImageOf(product, html),
            HasPriceRange: hasRange,
            OptionLabels: OptionLabels(html));
    }

    internal static Outcome ToScrapedProduct(ProductPage page, BigCommerceStore store, string seller)
    {
        if (page.Price is not { } price || price < MinimumPrice)
            return Outcome.Skip(SkipReason.NoPrice);

        // A retailer's product without a brand would be filed under the retailer's
        // own name, a brand that doesn't exist.
        if (string.IsNullOrWhiteSpace(page.Brand))
            return Outcome.Skip(SkipReason.NoBrand);

        if (store.OwnLabelPrefixes?.Any(p => page.Brand.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true)
            return Outcome.Skip(SkipReason.OwnLabel);

        if (page.HasPriceRange)
            return Outcome.Skip(SkipReason.PriceRange);

        if (ExpiredRegex().IsMatch(page.Name) || page.OptionLabels.Any(l => ExpiredRegex().IsMatch(l)))
            return Outcome.Skip(SkipReason.Expired);

        if (NonSupplementProductFilter.IsAccessoryOrApparel(page.Name))
            return Outcome.Skip(SkipReason.Accessory);

        var size = SingleSize(page.OptionLabels);
        var fullName = size is null || page.Name.Contains(size, StringComparison.OrdinalIgnoreCase)
            ? page.Name
            : $"{page.Name} - {size}";

        // The category comes from the product's own name, not the option label:
        // "Now CLA 800 mg - 180 Gels" read as an energy gel (softgels), and a flavour
        // in the label could look like a snack.
        var category = ProductAttributeParser.InferCategory(page.Name, page.Brand)
            ?? ProductAttributeParser.InferCategory(page.Name);
        if (store.OnlyCategories is { } allowed && (category is null || !allowed.Contains(category)))
            return Outcome.Skip(SkipReason.Category);

        return new Outcome(new ScrapedProduct(
            Name: fullName,
            Url: page.Url,
            ImageUrl: page.ImageUrl,
            Category: category,
            Price: price,
            StoreOldPrice: page.RegularPrice > price ? page.RegularPrice : null,
            ServingsPerPackage: ProductAttributeParser.ExtractServings(fullName),
            BrandName: page.Brand!.Trim(),
            InStock: page.InStock,
            Seller: seller), null);
    }

    /// <summary>
    /// The one package size the options state, or null when they state none or
    /// several. Flavour options repeat the size ("Grape Splash 30 Servings").
    /// </summary>
    internal static string? SingleSize(IReadOnlyList<string> labels)
    {
        var sizes = labels
            .Select(l => SizeRegex().Match(l))
            .Where(m => m.Success)
            .Select(m => WhitespaceRegex().Replace(m.Value.Trim(), " "))
            .GroupBy(s => s.TrimEnd('.').ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

        return sizes.Count == 1 ? sizes[0] : null;
    }

    private sealed record Attributes(
        decimal? Price, string? Currency, decimal? NonSalePrice, decimal? RetailPrice, bool? InStock, bool HasRange);

    private static Attributes? ReadAttributes(string html)
    {
        var match = BcDataRegex().Match(html);
        if (!match.Success)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (!doc.RootElement.TryGetProperty("product_attributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            decimal? price = null, nonSale = null, retail = null, rangeMin = null, rangeMax = null;
            string? currency = null;
            if (attributes.TryGetProperty("price", out var prices) && prices.ValueKind == JsonValueKind.Object)
            {
                (price, currency) = Money(prices, "without_tax");
                (nonSale, _) = Money(prices, "non_sale_price_without_tax");
                (retail, _) = Money(prices, "rrp_without_tax");
                if (prices.TryGetProperty("price_range", out var range) && range.ValueKind == JsonValueKind.Object)
                {
                    if (range.TryGetProperty("min", out var min))
                        (rangeMin, _) = Money(min, "without_tax");
                    if (range.TryGetProperty("max", out var max))
                        (rangeMax, _) = Money(max, "without_tax");
                }
            }

            bool? inStock = attributes.TryGetProperty("instock", out var stock)
                && stock.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? stock.GetBoolean()
                : null;

            return new Attributes(price, currency, nonSale, retail, inStock,
                HasRange: rangeMin is not null && rangeMax is not null && rangeMin != rangeMax);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (decimal? Value, string? Currency) Money(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object)
            return (null, null);

        decimal? value = money.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal()
            : null;
        var currency = money.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;
        return (value, currency);
    }

    private static JsonElement? FindProduct(string html)
    {
        foreach (Match match in JsonLdRegex().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                if (FindProduct(doc.RootElement) is { } product)
                    return product.Clone();
            }
            catch (JsonException)
            {
                // A broken block elsewhere on the page doesn't hide the product's own.
            }
        }

        return null;
    }

    private static JsonElement? FindProduct(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (FindProduct(item) is { } found)
                    return found;
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (IsType(element, "Product"))
            return element;

        return element.TryGetProperty("@graph", out var graph) ? FindProduct(graph) : null;
    }

    private static bool IsType(JsonElement element, string type) =>
        element.TryGetProperty("@type", out var t) && t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), type, StringComparison.Ordinal),
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == type),
            _ => false,
        };

    private static JsonElement? FirstOffer(JsonElement product)
    {
        if (!product.TryGetProperty("offers", out var offers))
            return null;

        return offers.ValueKind switch
        {
            JsonValueKind.Object => offers,
            JsonValueKind.Array when offers.GetArrayLength() > 0 => offers[0],
            _ => null,
        };
    }

    private static string? BrandOf(JsonElement product, string html)
    {
        if (product.TryGetProperty("brand", out var brand))
        {
            var name = brand.ValueKind switch
            {
                JsonValueKind.String => brand.GetString(),
                JsonValueKind.Object => StringOf(brand, "name"),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(name))
                return Clean(name);
        }

        var shown = BrandLinkRegex().Match(html);
        return shown.Success ? Clean(shown.Groups[1].Value) : null;
    }

    private static string? ImageOf(JsonElement product, string html)
    {
        if (product.TryGetProperty("image", out var image))
        {
            var url = image.ValueKind switch
            {
                JsonValueKind.String => image.GetString(),
                JsonValueKind.Array when image.GetArrayLength() > 0 => image[0].ValueKind == JsonValueKind.String
                    ? image[0].GetString()
                    : StringOf(image[0], "url"),
                JsonValueKind.Object => StringOf(image, "url"),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(url))
                return url.Trim();
        }

        var og = OgImageRegex().Match(html);
        return og.Success ? WebUtility.HtmlDecode(og.Groups[1].Value).Trim() : null;
    }

    private static IReadOnlyList<string> OptionLabels(string html) =>
        SelectOptionRegex().Matches(html).Select(m => m.Groups[1].Value)
            .Concat(VariantLabelRegex().Matches(html).Select(m => m.Groups[1].Value))
            .Select(Clean)
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string StringOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static decimal? DecimalOf(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDecimal();

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static void Count(Dictionary<SkipReason, int> counts, SkipReason reason) =>
        counts[reason] = counts.GetValueOrDefault(reason) + 1;

    private static string Clean(string? text) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(text ?? ""), " ").Trim();

    private static string SellerFromBaseUrl(string baseUrl)
    {
        var host = new Uri(baseUrl).Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    [GeneratedRegex(@"<loc>\s*([^<\s]+)\s*</loc>")]
    private static partial Regex SitemapLocRegex();

    [GeneratedRegex(@"<script[^>]*application/ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdRegex();

    // "var BCData = {"product_attributes":{...}};" in a script of its own.
    [GeneratedRegex(@"var\s+BCData\s*=\s*(\{.*?\});\s*</script>", RegexOptions.Singleline)]
    private static partial Regex BcDataRegex();

    [GeneratedRegex(@"<option[^>]*data-product-attribute-value=""\d+""[^>]*>([^<]+)</option>", RegexOptions.IgnoreCase)]
    private static partial Regex SelectOptionRegex();

    [GeneratedRegex(@"<span[^>]*class=""form-option-variant[^""]*""[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex VariantLabelRegex();

    [GeneratedRegex(@"class=""productView-brand""[^>]*>.*?<span[^>]*>([^<]+)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BrandLinkRegex();

    [GeneratedRegex(@"<meta\s+property=""og:image""\s+content=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex OgImageRegex();

    // "Exp. 2/2024", "Exp 02/24", "Expires 3/2025".
    [GeneratedRegex(@"\bexp(?:\.|ires?|iration)?\s*:?\s*\d{1,2}\s*/\s*\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiredRegex();

    // A package size: "30 Servings", "25 Srv.", "200 Grams", "2 fl. oz.", "50 Veg Capsules".
    [GeneratedRegex(
        @"\b\d+(?:\.\d+)?\s*(?:servings?|srv\.?|serv\.?|grams?|gr|g|kg|lbs?\.?|pounds?|fl\.?\s?oz\.?|oz\.?|ml|" +
        @"(?:veg(?:gie|etarian)?\s+)?capsules?|caps|tablets?|tabs|softgels?|gels|gummies|chews|pouches|packets?|sticks?|count|ct)(?![A-Za-z])",
        RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
