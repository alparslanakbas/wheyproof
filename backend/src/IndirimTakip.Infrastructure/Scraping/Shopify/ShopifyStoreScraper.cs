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
    : IBrandScraper, IProductDetailFetcher
{
    // Only stores that print the nutrition panel as text on the product page
    // (see ShopifyStore.NutritionOnPage); the backfill skips every other store.
    public bool HasProductDetails => store.NutritionOnPage;

    /// <summary>
    /// Reads the Nutrition Facts panel from the product page's text. Published
    /// only when it passes the same calorie check as label images; the fields
    /// are the ones that check covers. No description: US pages don't show one.
    /// </summary>
    public async Task<ProductDetails> FetchDetailsAsync(string productUrl, CancellationToken cancellationToken = default)
    {
        var none = new ProductDetails(null, null, null);
        if (!store.NutritionOnPage)
            return none;

        var html = await httpClient.GetStringAsync(productUrl, cancellationToken);
        // Structured JSON first (Naked), then the visible panel text (Quest).
        var reading = NutritionLabels.PageNutritionJson.Read(html, HandleFrom(productUrl))
            ?? NutritionLabels.PageNutritionText.Read(html);
        if (reading is null)
            return none;

        var nutritionJson = NutritionParser.BuildNutritionJson(reading.Rows.Select(r => (r.Label, r.Amount)));
        return new ProductDetails(null, nutritionJson, reading.ProteinGrams, reading.ServingSizeGrams);
    }

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
        // Added with the 2026-09-14 stores: AnimalPak files 30 tees, bags and
        // shakers as "Apparel and Accessories", Bounce its shirts as "T-Shirt",
        // MusclePharm a storefront placeholder as "Hidden". Ultimate Paleo
        // lists reseller price sheets as "Wholesale": real rows, but not a
        // price a shopper can pay.
        "apparel and accessories", "t-shirt", "t-shirts", "hidden", "wholesale",
    };

    private static readonly HashSet<string> ColorOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "colour", "colors", "colours",
    };

    // An option that describes flavor rather than a different package. Matched
    // as a word, not as the whole name: stores qualify it ("Protein Flavor",
    // "Thavage Pre-Workout (Flavor)", "Whey Isolate Flavor - 30 servings/bag").
    // An exact-name list missed those, so every flavor combination of a stack
    // counted as a size and became its own product: 72 rows for one Raw
    // Nutrition stack, 50 for a Promix bundle (measured 2026-09-13).
    internal static bool IsFlavorOption(string optionName) => FlavorOptionRegex().IsMatch(optionName);

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

    // Nothing under a dollar on these stores is a supplement: Quest lists
    // loyalty rewards (a scarf, a watch) at $0.01 and Ascent sells "Shipping
    // Protection" at $0.75 (measured 2026-09-11). The floor also keeps a zero
    // price out of the discount maths.
    private const decimal MinimumPrice = 1m;

    // From 10 kg up a package is a wholesale drum: BulkSupplements sells up to
    // 30 kg, one of them at $68,474. The largest consumer tub in the survey was
    // a 20 lb gainer (9.07 kg), so the line sits just above it.
    private const decimal WholesaleGrams = 10_000m;

    internal static IEnumerable<ScrapedProduct> ToScrapedProducts(ShopifyProduct product, ShopifyStore store, string? seller)
    {
        if (IsExcluded(product, store))
            yield break;

        var title = product.Title.Trim();
        var brand = store.IsRetailer ? product.Vendor?.Trim() : null;

        // The store's product type ("PRE-WORKOUT", "Carb Powders") is a useful
        // second signal next to the name.
        var categoryText = string.IsNullOrWhiteSpace(product.ProductType) ? title : $"{title} {product.ProductType}";
        var category = InferCategory(product, categoryText, brand ?? store.BrandName);

        if (store.OnlyCategories is { } allowed && (category is null || !allowed.Contains(category)))
            yield break;

        var sizePositions = product.Options
            .Where(o => !IsFlavorOption(o.Name))
            .Select(o => o.Position)
            .OrderBy(p => p)
            .ToList();

        var groups = product.Variants
            .Where(v => v.Price >= MinimumPrice)
            .GroupBy(v => SizeKey(v, sizePositions))
            .ToList();

        var hasSizeDimension = groups.Any(g => g.Key.Length > 0);

        // Product-level: every size row shares the product's images.
        var labelImage = NutritionLabels.NutritionLabelImagePicker.Pick(product.Images.Select(i => i.Src));

        foreach (var group in groups)
        {
            var inStock = group.Where(v => v.Available).ToList();
            var chosen = (inStock.Count > 0 ? inStock : group.ToList()).MinBy(v => v.Price)!;
            var label = group.Key;

            var name = label.Length == 0 || title.Contains(label, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title} - {label}";

            if (ProductAttributeParser.ToGrams(ProductAttributeParser.ExtractSize(name)) >= WholesaleGrams)
                continue;

            // A size-specific link lands the shopper on that size. Products with
            // no size dimension keep the plain URL.
            //
            // THE URL IS THE ROW'S IDENTITY across crawls (ingestion matches on
            // it), so the variant in it must not move. The cheapest in-stock
            // variant does move: a flavor sells out or goes on sale, the link
            // changes, and the next crawl opens a new product while the old row
            // goes stale with the price history. The group's lowest variant id
            // is stable and still lands on the same size; the price keeps coming
            // from the cheapest in-stock flavor.
            var url = hasSizeDimension
                ? $"{store.BaseUrl}/products/{product.Handle}?variant={group.Min(v => v.Id)}"
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
                ServingsPerPackage: ProductAttributeParser.ExtractServings(label.Length > 0 ? label : title),
                NutritionLabelImageUrl: labelImage);
        }
    }

    private static bool IsExcluded(ShopifyProduct product, ShopifyStore store)
    {
        var type = product.ProductType?.Trim();
        if (!string.IsNullOrEmpty(type) && ExcludedProductTypes.Contains(type))
            return true;

        // Ghost keeps a hidden parent record per product line ("GHOST® WHEY")
        // next to the real listings, one per flavor. The parent has no image,
        // no product type and duplicates the flavored listings (67 of 492
        // products, all image-less, measured 2026-09-12).
        if (product.Tags.Any(t => t.Equals("base_product", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Structural apparel/merch signals, independent of the title's wording.
        // Measured across all 18 stores on 2026-09-12: a "Color" option matched
        // 174 products and letter sizes (S/M/L/XL) matched apparel and knee
        // sleeves only; neither matched a single supplement. Supplements vary
        // by flavor and weight or count, never by color or letter size.
        if (product.Options.Any(o => ColorOptionNames.Contains(o.Name.Trim())))
            return true;

        if (product.Variants.Any(v => IsLetterSize(v.Option1) || IsLetterSize(v.Option2) || IsLetterSize(v.Option3)))
            return true;

        // Retailers file their own gear under a vendor such as
        // "Bodybuilding.com Accessories" (weightlifting belts).
        if (store.IsRetailer && product.Vendor is { } vendor
            && (vendor.Contains("accessor", StringComparison.OrdinalIgnoreCase)
                || vendor.Contains("apparel", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Checkout add-ons sold as products: SavedBy "Package Protection" (24
        // price tiers each on Gorilla Mind and Raw Nutrition, up to $40.97) and
        // the Bodybuilding.com membership (measured 2026-09-13). The $1 floor
        // doesn't catch them. Sample packs are real, buyable products and stay.
        if (NonProductServiceRegex().IsMatch(product.Title))
            return true;

        return ApparelOrMerchRegex().IsMatch(product.Title)
            || NonSupplementProductFilter.IsAccessoryOrApparel(product.Title);
    }

    /// <summary>
    /// Category from the title and product type; two fallbacks ONLY when that
    /// finds nothing, so a product that already has a category never changes.
    /// </summary>
    /// <remarks>
    /// 1. Brand stripped (the normal rule): a retailer's "Proteinocean Creatine"
    ///    must not become protein because of the brand name.
    /// 2. Brand kept: when the brand's own name is the product word, stripping
    ///    leaves nothing. "Ultimate Paleo Protein, Vanilla" became ", Vanilla"
    ///    and 18 of that store's 40 rows had no category (measured 2026-09-14).
    /// 3. Tags that SAY they are a category: AnimalPak names products "Animal
    ///    Fury" or "Animal Cuts" and states the kind only as "Category:Pre
    ///    Workout" or "Category: Fat Burners".
    ///
    /// <b>Free tags were tried and dropped.</b> Using every tag categorised 242
    /// rows across the existing stores in a before/after crawl, and many were
    /// wrong: Naked Fiber, Naked Reds and a mushroom blend as protein powder,
    /// Ghost digestive enzymes as pre-workout, a face cream as vitamins, a
    /// coffee as hydration. It also let 262 herbal extracts through
    /// BulkSupplements' sport-only filter. Stores tag for marketing, not
    /// taxonomy, so an unlabelled tag says nothing about the product.
    /// </remarks>
    private static string? InferCategory(ShopifyProduct product, string categoryText, string brandName)
    {
        var category = ProductAttributeParser.InferCategory(categoryText, brandName)
            ?? ProductAttributeParser.InferCategory(categoryText);

        if (category is not null)
            return category;

        var declared = product.Tags
            .Select(t => CategoryTagRegex().Match(t))
            .Where(m => m.Success)
            .Select(m => m.Groups["kind"].Value);

        return ProductAttributeParser.InferCategory(string.Join(" ", declared), brandName);
    }

    // "Category:Pre Workout", "Category: Fat Burners".
    [GeneratedRegex(@"^\s*category\s*:\s*(?<kind>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryTagRegex();

    // ".../products/double-chocolate-whey-protein-2lb?variant=123" -> the handle.
    internal static string HandleFrom(string productUrl)
    {
        var path = new Uri(productUrl).AbsolutePath;
        var marker = path.IndexOf("/products/", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? string.Empty : path[(marker + "/products/".Length)..].Trim('/');
    }

    private static bool IsLetterSize(string? value) =>
        value is not null && LetterSizeRegex().IsMatch(value.Trim());

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

    [GeneratedRegex(@"\b(package|shipping|order|delivery|route)\s+(protection|insurance)\b|\bmembership\b|\bwarranty\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonProductServiceRegex();

    [GeneratedRegex(@"\b(flavors?|flavours?|tastes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlavorOptionRegex();

    [GeneratedRegex(@"Shopify\.currency\s*=\s*\{\s*""active""\s*:\s*""(?<code>[A-Za-z]{3})""")]
    private static partial Regex StorefrontCurrencyRegex();

    // A whole option value that is a clothing size. Anchored, so "Large Tub"
    // or "5 lb" never match.
    [GeneratedRegex(@"^(xxs|xs|s|m|l|xl|xxl|xxxl|[2-5]xl|small|medium|large|x-large|xx-large|xxx-large)$", RegexOptions.IgnoreCase)]
    private static partial Regex LetterSizeRegex();

    // English apparel and merch words, as whole words. Some words need their
    // qualifier because they also appear in supplement titles:
    // - "caps" alone means capsules ("Ashwagandha 120 Caps"), so only dad,
    //   swim and baseball caps match;
    // - "bottle" is a count ("Magnesium Glycinate 1 Bottle"), so only drink
    //   bottles match;
    // - "cup" is food ("PB Cup Nut Butter") and "cooler" can be a flavor name;
    // - "tumbler" is left out: Transparent Labs bundles a real tub with one.
    [GeneratedRegex(@"\b((t-?)?shirts?|button\s*downs?|tees?|tank\s*tops?|tanks?|hoodies?|crewnecks?|sweatshirts?|sweatpants|joggers?|shorts|socks|hats?|beanies?|snapbacks?|(baseball|dad|swim)\s*caps?|headbands?|jackets?|leggings|sports?\s*bras?|apparel|towels?|backpacks?|duff(el|le)s?|gym\s*bags?|mystery\s*(bottles?|box(es)?|bags?)|cooler\s*bags?|retro\s*cooler|totes?|stickers?|posters?|keychains?|lockbox(es)?|scarf|scarves|watch(es)?|(exercise|resistance)\s*bands?|(weight)?lifting\s*belts?|gift\s*cards?|shipping\s*protection|free\s*shipping|shakers?|(blender|water|sport|squeeze|trimr|classic)\s*bottles?|jugs?|mugs?|(metal|enamel)\s*cups?|crunchcup|pill\s*(cases?|organizers?)|funnels?|lanyards?|empty\s*capsules|pantry|drinkware)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ApparelOrMerchRegex();
}
