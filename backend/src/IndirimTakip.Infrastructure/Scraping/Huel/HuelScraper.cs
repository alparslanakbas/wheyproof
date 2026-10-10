using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Huel;

/// <summary>
/// Huel, US and UK: read from its own product pages, one request per listed product.
/// </summary>
/// <remarks>
/// <b>Why not products.json.</b> Until 2026-10-08 the catalog came from the Shopify backends behind
/// the public sites (huelamerica / hibble.myshopify.com). That day Huel took its products off the
/// Online Store channel: products.json answered with 7 and 4 leftovers (outlet, merch, a test item)
/// and every product's .js address 404s, while huel.com kept selling. Both editions went stale.
///
/// <b>Where the data is.</b> huel.com is a Next.js site. Each product page embeds the storefront's
/// product object in its React payload (<c>self.__next_f.push</c>): title, handle, isBundle and
/// every variant with its Shopify variant id, one-time and subscription price in cents, stock,
/// servings and, for a different package, its own delivery unit. The page's JSON-LD lists the
/// variants with price and currency but without the Shopify ids and package, so it can't rebuild
/// the rows; it is used to CHECK the currency and the price unit (see <see cref="ReadPage"/>).
///
/// <b>Same rows as before.</b> The product is turned into the products.json shape and goes through
/// <see cref="ShopifyStoreScraper.ToScrapedProducts"/>, so naming, category, filters and the
/// address rule (<c>?variant=</c> with the group's lowest id) are the Shopify scraper's own. A
/// variant that names its own delivery unit is another package (Black Edition's 16-pack box and
/// 3-bag trio); the others are flavors of the product's package. A bundle keeps its variant in
/// the address, as products.json gave it. Measured against the database on 2026-10-10: 22 of the
/// 24 US rows and 27 of the 32 UK rows came out with the same address and price; the rest are
/// packages the pages no longer offer (they go stale) and one UK row whose last second size is
/// gone (Diet powder, a plain address now). Rows named by a size label change name: the label
/// products.json had ("17 meals / 1 bag") is not on the page, so the package is named by its
/// servings. The address, not the name, is the row's identity.
///
/// <b>Market.</b> huel.com and uk.huel.com send our Frankfurt server to de.huel.com (a 307 that
/// sets <c>huel_locale</c>). The page is served for the edition when the request carries that
/// cookie with the edition's locale, the same thing the site's country picker stores. Redirects
/// are off in this client: if the cookie stops being honoured the page fails instead of being
/// read as the German store, and a page whose JSON-LD names another currency stops the run.
/// </remarks>
/// <param name="requestDelay">Pause between requests; tests pass zero.</param>
public sealed partial class HuelScraper(
    HttpClient httpClient, ShopifyStore store, ILogger<HuelScraper> logger, TimeSpan? requestDelay = null) : IBrandScraper
{
    public const string HttpClientName = "huel";

    // Listed by handle because most of the catalog has no page of its own: of the 48 US rows
    // and 86 UK pages products.json gave, only these answered 200 when opened as a shopper of
    // that country (2026-09-21); the rest are single units and bundle parts sold only through
    // the cart builders. The sitemap is no list either: it omits five of them. Two US handles
    // changed on 2026-10-08 and are left out: fiber-boost-bundle 404s, and huel-bestseller-bundle
    // shows bestseller-bundle's product (the same variant), which would make a second row of it.
    internal static readonly ShopifyStore Us = new("Huel", "https://huel.com",
        OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "huel", "huel-black-edition", "black-edition-10-meals", "huel-essential",
            "huel-complete-protein", "huel-daily-superblend", "huel-daily-greens",
            "huel-daily-greens-ready-to-drink", "huel-ready-to-drink",
            "huel-black-edition-ready-to-drink", "huel-bar", "huel-energy-plus",
            "huel-instant-meal-pots", "hot-and-savoury-meal-packs", "bestseller-bundle",
            "breakfast-lunch-bundle", "discovery-bundle-v2", "glp1-companion-pack",
            "high-protein-starter-kit", "huel-high-protein-bundle",
        });

    internal static readonly ShopifyStore Uk = new("Huel", "https://uk.huel.com", Market: SiteMarket.Uk,
        OnlyHandles: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "huel", "huel-black-edition", "black-edition-10-meals", "black-edition-10-meals-banana",
            "black-edition-bulk-save", "huel-essential", "huel-complete-protein", "huel-professional",
            "huel-gluten-free", "huel-diet-powder", "huel-daily-greens", "huel-daily-greens-ready-to-drink",
            "huel-daily-a-z-vitamins", "huel-ready-to-drink", "huel-black-edition-ready-to-drink",
            "huel-lite-ready-to-drink", "huel-bar", "hot-and-savoury-meal-packs",
            "hot-and-savoury-black-edition-ramen", "hot-and-savoury-lite-ramen", "huel-bestseller-bundle",
            "huel-taster-bundle", "breakfast-lunch-bundle", "daily-wellness-set", "high-protein-starter-kit",
            "huel-high-protein-bundle", "light-lean-bundle",
        });

    public static ShopifyStore ForMarket(SiteMarket market) => market == SiteMarket.Uk ? Uk : Us;

    private const string ProductMarker = "\"product\":{\"productId\"";
    private const string Undefined = "$undefined";

    private readonly TimeSpan delay = requestDelay ?? TimeSpan.FromSeconds(2);

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    private string Locale => store.StoreMarket == SiteMarket.Uk ? "en-GB" : "en-US";

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var handles = store.OnlyHandles!.Order(StringComparer.Ordinal).ToList();
        var result = new List<ScrapedProduct>();
        var failed = 0;

        foreach (var handle in handles)
        {
            await Task.Delay(delay, cancellationToken);
            var address = $"{store.BaseUrl}/products/{handle}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.Add("Cookie", $"huel_locale={Locale}");
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failed++;
                    logger.LogWarning("Huel: {Url} answered {Status}{Location}; skipped.", address, (int)response.StatusCode,
                        response.Headers.Location is { } to ? $" (to {to})" : "");
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (ReadPage(html, handle, store.StoreMarket.Currency) is not { } product)
                {
                    failed++;
                    logger.LogWarning("Huel: {Url} has no readable product data; skipped.", address);
                    continue;
                }

                result.AddRange(ShopifyStoreScraper.ToScrapedProducts(product, store, seller: null));
            }
            catch (HttpRequestException ex)
            {
                failed++;
                logger.LogWarning(ex, "Huel: product page skipped: {Url}", address);
            }
        }

        // Half the pages failing is not a few withdrawn products: the run is wrong and
        // must not pass as a smaller catalog.
        if (failed * 2 > handles.Count)
            throw new InvalidOperationException($"Huel: {failed} of {handles.Count} product pages failed.");

        logger.LogInformation("Huel: {Records} records from {Pages} product pages ({Failed} skipped).",
            result.Count, handles.Count, failed);
        return result;
    }

    /// <summary>
    /// The page's product in the products.json shape, or null when the page doesn't hold one that
    /// can be trusted.
    /// </summary>
    /// <remarks>
    /// Two checks against the page's own JSON-LD, each guarding a silent failure. The currency must
    /// be the edition's; anything else throws, because a wrong-currency price must never reach the
    /// site. And the embedded one-time price, read in cents, must match one of the JSON-LD prices:
    /// if the field changes unit or meaning (a subscription price, a per-meal price) the page is
    /// skipped instead of publishing prices off by a factor.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The page states another currency.</exception>
    internal static ShopifyProduct? ReadPage(string html, string handle, string currency)
    {
        var (currencies, prices, category) = ReadStructuredData(html);
        if (currencies.Count == 0)
            return null;
        if (currencies.Any(c => !c.Equals(currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Huel: the {handle} page is priced in {string.Join("/", currencies)}, not {currency}. Skipped so that prices in the wrong currency never reach the site.");

        if (FindEmbeddedProduct(html, handle) is not { } embedded)
            return null;

        var product = ToShopifyProduct(embedded, handle, category);
        if (product is null || product.Variants.Count == 0)
            return null;

        return prices.Contains(product.Variants.Min(v => v.Price)) ? product : null;
    }

    /// <summary>
    /// Currencies and prices of the JSON-LD Product / ProductGroup offers, and the product's
    /// category there ("Nutritionally Complete Powders"), which stands in for Shopify's product type.
    /// </summary>
    private static (HashSet<string> Currencies, HashSet<decimal> Prices, string? Category) ReadStructuredData(string html)
    {
        var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prices = new HashSet<decimal>();
        string? category = null;
        foreach (Match match in JsonLdRegex().Matches(html))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(match.Groups[1].Value);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || Text(root, "@type") is not ("Product" or "ProductGroup"))
                    continue;
                category ??= Text(root, "category");

                var items = root.TryGetProperty("hasVariant", out var variants) && variants.ValueKind == JsonValueKind.Array
                    ? variants.EnumerateArray().ToList()
                    : [root];
                foreach (var item in items)
                {
                    if (!item.TryGetProperty("offers", out var offers))
                        continue;
                    foreach (var offer in offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToList() : [offers])
                    {
                        if (Text(offer, "priceCurrency") is { } c)
                            currencies.Add(c);
                        if (Text(offer, "price") is { } p
                            && decimal.TryParse(p, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                            prices.Add(price);
                    }
                }
            }
        }
        return (currencies, prices, category);
    }

    /// <summary>The embedded product object of <paramref name="handle"/> from the React payload.</summary>
    private static JsonElement? FindEmbeddedProduct(string html, string handle)
    {
        var payload = new StringBuilder();
        foreach (Match match in PayloadChunkRegex().Matches(html))
        {
            try
            {
                payload.Append(JsonSerializer.Deserialize<string>(match.Groups[1].Value));
            }
            catch (JsonException)
            {
                // A chunk that isn't a plain JSON string literal holds no product data.
            }
        }

        var text = payload.ToString();
        var products = new List<JsonElement>();
        for (var at = text.IndexOf(ProductMarker, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(ProductMarker, at + 1, StringComparison.Ordinal))
        {
            var start = at + "\"product\":".Length;
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
            try
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                products.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
            }
        }

        // The embedded handle is not always the page's: glp1-companion-pack writes
        // "/glp1-companion-pack", and the UK huel-daily-greens-ready-to-drink page holds
        // "daily-greens-ready-to-drink" (2026-10-10). A page with other products in it
        // (recommendations) must name the one wanted; a page with one product is that product.
        var named = products.Where(p => string.Equals(Text(p, "handle")?.TrimStart('/'), handle,
            StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count > 0 ? named[0] : products.Count == 1 ? products[0] : null;
    }

    /// <summary>
    /// The embedded product as products.json would describe it (see the class remarks). The handle
    /// is the page's, the address shoppers are sent to, whatever the payload calls the product.
    /// </summary>
    private static ShopifyProduct? ToShopifyProduct(JsonElement embedded, string handle, string? productType)
    {
        if (Text(embedded, "title") is not { } title
            || !embedded.TryGetProperty("variants", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        if (embedded.TryGetProperty("isMerchProduct", out var merch) && merch.ValueKind == JsonValueKind.True)
            return null;

        var variants = new List<(JsonElement Raw, long Id, decimal Price, string Name, bool OwnPackage)>();
        foreach (var v in list.EnumerateArray())
        {
            if (Text(v, "shopifyVariantGuid") is not { } guid || !long.TryParse(VariantIdRegex().Match(guid).Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || !v.TryGetProperty("pricing", out var pricing) || Cents(pricing, "onetimePrice") is not { } price)
                continue;
            // A bundle's only variant may have no name (discovery-bundle-v2); it is the product itself.
            variants.Add((v, id, price, Text(v, "name") ?? title, Text(v, "deliveryUnit") is not null));
        }

        var bySize = variants.Any(v => v.OwnPackage) || Flag(embedded, "isBundle");
        var flavors = variants.Where(v => !v.OwnPackage).ToList();
        // The label shared by the flavors of the product's own package: a bundle's single
        // variant names it ("Huel Discovery Bundle v2"), otherwise its servings when they agree
        // and are whole (Essential's 22.5 would read as 5 to the servings parser).
        var servings = flavors.Select(v => Number(v.Raw, "servingQuantity")).Distinct().ToList();
        var packageLabel = flavors.Count == 1 ? flavors[0].Name
            : servings is [{ } count] && count == decimal.Truncate(count) ? $"{count:0} servings"
            : title;

        var product = new ShopifyProduct
        {
            Title = title,
            Handle = handle,
            ProductType = productType,
            Options = bySize
                ? [new ShopifyOption { Name = "Size", Position = 1 }, new ShopifyOption { Name = "Flavor", Position = 2 }]
                : [new ShopifyOption { Name = "Flavor", Position = 1 }],
            Images = Text(embedded, "featuredImage") is { } image ? [new ShopifyImage { Src = image }] : [],
        };

        foreach (var v in variants)
        {
            product.Variants.Add(new ShopifyVariant
            {
                Id = v.Id,
                Price = v.Price,
                Available = !Flag(v.Raw, "isOutOfStock") && !Flag(v.Raw, "isComingSoon"),
                Option1 = !bySize ? v.Name : v.OwnPackage ? v.Name : packageLabel,
                Option2 = bySize && !v.OwnPackage ? v.Name : null,
            });
        }
        return product;
    }

    // The payload writes a missing value as the string "$undefined", also in number fields
    // (a UK product's subscriptionPrice), so every read is tolerant of it.
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text && text != Undefined
            ? text
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static decimal? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static decimal? Cents(JsonElement element, string name) =>
        Number(element, name) is { } cents && cents > 0 ? cents / 100m : null;

    [GeneratedRegex(@"<script[^>]*application/ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex(@"self\.__next_f\.push\(\[1,(""(?:[^""\\]|\\.)*"")\]\)")]
    private static partial Regex PayloadChunkRegex();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex VariantIdRegex();
}
