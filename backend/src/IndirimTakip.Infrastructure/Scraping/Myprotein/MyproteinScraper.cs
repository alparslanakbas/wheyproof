using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Myprotein;

/// <summary>
/// myprotein.com, UK section only. THG's own platform, not Shopify.
/// </summary>
/// <remarks>
/// <b>Where the data is.</b> Each product page embeds the storefront's product
/// object as a script variable, <c>const masterData = {...}</c>: every variant
/// with title, SKU, barcode, stock, GBP price and RRP, and its options (Flavour,
/// Amount). The JSON-LD on the same page lists the variants too but WITHOUT the
/// amount, so it can't tell 450 g from 2.5 kg. Product addresses come from the
/// nutrition category listings (<c>?pageNumber=N</c>); the sitemap answers 403.
///
/// <b>One record per amount, flavours collapsed (the Shopify rule).</b> Myprotein
/// sells by servings and the same tier weighs differently per flavour (30
/// servings is 900 g for one flavour and 870 g for another): Impact Whey had 119
/// variants over 51 amounts on 2026-10-03. Every amount is its own record, so its
/// weight and therefore its price history never change; one record per servings
/// tier would switch flavours (and weight) whenever the cheapest one changed and
/// fake discounts. Decided with the owner. The price is the cheapest in-stock
/// flavour of that amount, and the address carries the group's lowest SKU so it
/// stays the same when that flavour changes.
///
/// <b>Market.</b> Each page states the market it was served for. The UK store is
/// served to the server's address (Frankfurt) as well; a page for any other
/// country fails the run instead of being ingested.
///
/// <b>Once a day.</b> 236 products in nine categories (2026-10-03), one page each
/// (about 1 MB unpacked, 120 kB compressed). Every six hours would be heavy on
/// their server for prices that change daily at most.
/// </remarks>
/// <param name="requestDelay">Pause between requests; tests pass zero.</param>
public sealed partial class MyproteinScraper(
    HttpClient httpClient, ILogger<MyproteinScraper> logger, TimeSpan? requestDelay = null) : IBrandScraper, IProductDetailFetcher
{
    public const string HttpClientName = "myprotein";

    // Nutrition categories in scope, measured 2026-10-03 (products / pages):
    // protein 66/4, creatine 29/2, pre-workout 36/2, carbohydrates 32/2,
    // amino-acids 33/2, high-protein food 52/3, endurance 50/3, energy 70/5,
    // vitamins 27/2; 236 distinct products. Accessories and clothing live in other
    // categories and are filtered again by name.
    internal static readonly string[] Categories =
    [
        "protein", "creatine", "pre-workout", "carbohydrates", "amino-acids",
        "healthy-food-drinks/high-protein", "endurance", "energy", "vitamins",
    ];

    private const int MaxPagesPerCategory = 20;
    private const decimal MinimumPrice = 1m;
    // Same cut as the Shopify stores: trade sizes are not a shopper's purchase.
    private const decimal WholesaleGrams = 10_000m;
    private const string MasterDataMarker = "const masterData = ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly TimeSpan delay = requestDelay ?? TimeSpan.FromSeconds(2);

    public string BrandName => "Myprotein";
    public string BaseUrl => "https://www.myprotein.com";
    public bool DailyOnly => true;

    /// <summary>
    /// The nutrition table of the flavour a record links to, for the detail backfill.
    /// </summary>
    /// <remarks>
    /// <b>The values differ by flavour.</b> A page prints one table per flavour and shows the selected
    /// one first ("Nutritional values will vary depending on the selected flavour", their own words):
    /// on 2026-10-07 Impact Whey Isolate's first table gave 80 g protein per 100 g for the flavour in a
    /// record's address and 82 g when fetched for another. The selected flavour is the page's
    /// activeVariant, which follows <c>?variation=</c>, and a record's address carries the lowest SKU of
    /// its amount, so the first table is the panel a shopper sees on arriving from our link. If the page
    /// answers with another variant (a SKU it no longer sells), the first table is another flavour's and
    /// nothing is read.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The page was served for another market; retried later.</exception>
    public async Task<ProductDetails> FetchDetailsAsync(string productUrl, CancellationToken cancellationToken = default)
    {
        var none = new ProductDetails(null, null, null);
        var html = await httpClient.GetStringAsync(productUrl, cancellationToken);
        var data = ReadMasterData(html);
        if (data is null || VariationRegex().Match(productUrl) is not { Success: true } variation)
            return none;

        if (!string.Equals(data.UserCountry, "GB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Myprotein: page served for {data.UserCountry ?? "an unknown country"}, not GB ({productUrl}).");

        if (data.ActiveVariant?.Sku.ToString(System.Globalization.CultureInfo.InvariantCulture) != variation.Groups[1].Value)
            return none;

        // The shown flavour's table sits in the "Nutritional Information" panel.
        var panel = html.IndexOf(NutritionPanelMarker, StringComparison.Ordinal);
        if (panel < 0 || NutritionLabels.UkNutritionTable.Read(html[panel..]) is not { } reading)
            return none;

        var nutritionJson = NutritionParser.BuildNutritionJson(reading.Rows.Select(r => (r.Label, r.Amount)));
        return new ProductDetails(null, nutritionJson, reading.ProteinGrams, reading.ServingSizeGrams);
    }

    private const string NutritionPanelMarker = "id=\"nutritionalinfo\"";

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var addresses = await CollectProductAddressesAsync(cancellationToken);
        if (addresses.Count == 0)
            throw new InvalidOperationException("Myprotein: the category listings returned no products; the site may have changed.");

        var result = new List<ScrapedProduct>();
        var failed = 0;
        foreach (var address in addresses)
        {
            await Task.Delay(delay, cancellationToken);
            try
            {
                using var response = await httpClient.GetAsync(address, cancellationToken);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                // After redirects: older listing slugs point at the canonical address.
                var productUrl = response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) ?? address;
                result.AddRange(ToScrapedProducts(html, productUrl));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
            {
                failed++;
                logger.LogWarning(ex, "Myprotein: product page skipped: {Url}", address);
            }
        }

        // Half the pages failing is not a few odd products: the run is wrong and
        // must not pass as a smaller catalog.
        if (failed * 2 > addresses.Count)
            throw new InvalidOperationException($"Myprotein: {failed} of {addresses.Count} product pages failed.");

        logger.LogInformation("Myprotein: {Records} records from {Pages} product pages ({Failed} skipped).",
            result.Count, addresses.Count, failed);
        return result;
    }

    private async Task<List<string>> CollectProductAddressesAsync(CancellationToken cancellationToken)
    {
        var addresses = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in Categories)
        {
            // Per category: a product already found under another category still
            // counts as this page's content, otherwise overlap would end paging early.
            var inCategory = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 1; page <= MaxPagesPerCategory; page++)
            {
                var url = $"{BaseUrl}/c/nutrition/{category}/" + (page > 1 ? $"?pageNumber={page}" : "");
                var html = await httpClient.GetStringAsync(url, cancellationToken);
                await Task.Delay(delay, cancellationToken);

                var added = 0;
                foreach (Match match in ProductPathRegex().Matches(html))
                {
                    var address = BaseUrl + match.Value;
                    if (!inCategory.Add(address))
                        continue;
                    added++;
                    if (seen.Add(address))
                        addresses.Add(address);
                }

                if (added == 0)
                    break;
            }
        }

        return addresses;
    }

    /// <summary>The records of one product page.</summary>
    /// <exception cref="InvalidDataException">The page carries no product data.</exception>
    /// <exception cref="InvalidOperationException">The page was served for another market.</exception>
    internal static IReadOnlyList<ScrapedProduct> ToScrapedProducts(string html, string productUrl)
    {
        var data = ReadMasterData(html)
                   ?? throw new InvalidDataException($"Myprotein: no product data on {productUrl}.");

        if (!string.Equals(data.UserCountry, "GB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Myprotein: page served for {data.UserCountry ?? "an unknown country"}, not GB ({productUrl}). " +
                "Stopped so that prices from another market never reach the site.");

        var title = (data.PageTitle ?? string.Empty).Trim();
        if (title.Length == 0 || NonSupplementProductFilter.IsAccessoryOrApparel(title))
            return [];

        // Myprotein also sells other brands' products (Aduna, KIKI Health, Cadence,
        // Freja... about 14 products in the 2026-10-03 dry run). Those are filed
        // under their own brand with Myprotein as the seller, the retailer rule of
        // the Shopify stores; Myprotein's own products stay under Myprotein.
        var brand = data.Brand?.Trim();
        var otherBrand = !string.IsNullOrEmpty(brand) && !brand.Equals("Myprotein", StringComparison.OrdinalIgnoreCase);

        var priced = data.Variants
            .Where(v => v.Price?.Price is { Currency: "GBP" } p && p.Amount >= MinimumPrice)
            .ToList();

        var result = new List<ScrapedProduct>();
        foreach (var group in priced.GroupBy(SizeKey))
        {
            var inStock = group.Where(v => v.InStock).ToList();
            var chosen = (inStock.Count > 0 ? inStock : group.ToList()).MinBy(v => v.Price!.Price!.Amount)!;

            var name = group.Key.Length == 0 || title.Contains(group.Key, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title} - {group.Key}";

            if (ProductAttributeParser.ToGrams(ProductAttributeParser.ExtractSize(name)) >= WholesaleGrams)
                continue;

            var price = chosen.Price!.Price!.Amount;
            var rrp = chosen.Price.Rrp is { Currency: "GBP" } r ? r.Amount : (decimal?)null;

            result.Add(new ScrapedProduct(
                Name: name,
                Url: $"{productUrl}?variation={group.Min(v => v.Sku)}",
                ImageUrl: chosen.Images.FirstOrDefault()?.Original ?? data.DefaultImages.FirstOrDefault()?.Original,
                Category: null,
                Price: price,
                StoreOldPrice: rrp > price ? rrp : null,
                BrandName: otherBrand ? brand : null,
                InStock: inStock.Count > 0,
                Seller: otherBrand ? "myprotein.com" : null));
        }

        return result;
    }

    // Every option that isn't a flavour is part of the size (the Shopify rule);
    // here that is "Amount", e.g. "900G - 30servings". Flavour options are told
    // apart the Shopify way, as a word: twin packs and mix boxes pick two
    // ("Flavour 1", "Flavour 2"), and an exact-name match made every flavour pair
    // its own record (36 for one Layered Protein Bar box in the 2026-10-03 dry run).
    private static string SizeKey(MyproteinVariant variant) =>
        string.Join(" - ", variant.Choices
            .Where(c => !Shopify.ShopifyStoreScraper.IsFlavorOption(c.OptionKey))
            .OrderBy(c => c.OptionKey, StringComparer.Ordinal)
            .Select(c => c.Key.Trim()));

    private static MyproteinMasterData? ReadMasterData(string html)
    {
        var start = html.IndexOf(MasterDataMarker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        // The object is followed by more script; the reader stops after one value.
        var bytes = Encoding.UTF8.GetBytes(html[(start + MasterDataMarker.Length)..]);
        var reader = new Utf8JsonReader(bytes);
        return JsonSerializer.Deserialize<MyproteinMasterData>(ref reader, JsonOptions);
    }

    [GeneratedRegex(@"/p/[a-z0-9-]+/[a-z0-9-]+/[0-9]+/")]
    private static partial Regex ProductPathRegex();

    [GeneratedRegex(@"[?&]variation=(\d+)")]
    private static partial Regex VariationRegex();
}

internal sealed class MyproteinMasterData
{
    [JsonPropertyName("pageTitle")]
    public string? PageTitle { get; set; }

    [JsonPropertyName("userCountry")]
    public string? UserCountry { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("defaultImages")]
    public List<MyproteinImage> DefaultImages { get; set; } = [];

    [JsonPropertyName("variants")]
    public List<MyproteinVariant> Variants { get; set; } = [];

    /// <summary>The variant the page was served for (<c>?variation=</c>); its table is shown first.</summary>
    [JsonPropertyName("activeVariant")]
    public MyproteinActiveVariant? ActiveVariant { get; set; }
}

// Only the SKU: the price scrape reads the same object, and a field of the active variant shaped
// differently from the variants' must not be able to fail it.
internal sealed class MyproteinActiveVariant
{
    [JsonPropertyName("sku")]
    public long Sku { get; set; }
}

internal sealed class MyproteinVariant
{
    [JsonPropertyName("sku")]
    public long Sku { get; set; }

    [JsonPropertyName("inStock")]
    public bool InStock { get; set; }

    [JsonPropertyName("choices")]
    public List<MyproteinChoice> Choices { get; set; } = [];

    [JsonPropertyName("price")]
    public MyproteinPrices? Price { get; set; }

    [JsonPropertyName("images")]
    public List<MyproteinImage> Images { get; set; } = [];
}

internal sealed class MyproteinChoice
{
    [JsonPropertyName("optionKey")]
    public string OptionKey { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}

internal sealed class MyproteinPrices
{
    [JsonPropertyName("price")]
    public MyproteinMoney? Price { get; set; }

    [JsonPropertyName("rrp")]
    public MyproteinMoney? Rrp { get; set; }
}

internal sealed class MyproteinMoney
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

internal sealed class MyproteinImage
{
    [JsonPropertyName("original")]
    public string? Original { get; set; }
}
