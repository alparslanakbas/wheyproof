using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using IndirimTakip.Core.Scraping;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.PrestaShop;

/// <summary>A PrestaShop store we collect prices from.</summary>
/// <param name="CategoryPath">The category listing that holds the products we list.</param>
/// <param name="CurrencyId">
/// The store's own id for this market's currency. PrestaShop numbers currencies
/// per store, so it is read from the store (its currency switcher), not assumed.
/// </param>
public sealed record PrestaShopStore(
    string BrandName, string BaseUrl, string CategoryPath, int CurrencyId, SiteMarket? Market = null)
{
    public SiteMarket StoreMarket => Market ?? SiteMarket.Us;
}

public static class PrestaShopStores
{
    /// <summary>
    /// PrestaShop stores. The first one, measured 2026-09-26.
    /// </summary>
    public static readonly IReadOnlyList<PrestaShopStore> All =
    [
        // 365Rider: a Spanish triathlon retailer (Awin programme 26763). Only its
        // nutrition category is listed (271 products, 14 brands: 226ERS, Santa
        // Madre, Enervit, Nutrinovex, SiS, Maurten...); the rest of the store is
        // shoes, bikes and clothing. It prices in EUR by default and offers GBP
        // (id 2) and USD (id 8) in its switcher; the same product was €52.95,
        // £50.57 and $64.60, so the store converts at its own rate and these are
        // its own prices, not ours. Its shipping page says UK prices include VAT;
        // outside the EU the buyer pays local import charges at delivery, which,
        // like shipping costs elsewhere, the listed price doesn't include.
        new("365Rider", "https://www.365rider.com", "/en/93-nutrition", CurrencyId: 8),
        new("365Rider", "https://www.365rider.com", "/en/93-nutrition", CurrencyId: 2, Market: SiteMarket.Uk),
    ];

    /// <summary>The stores an instance of the given market scrapes.</summary>
    public static IEnumerable<PrestaShopStore> ForMarket(SiteMarket market) =>
        All.Where(s => s.StoreMarket == market);
}

/// <summary>
/// Reads a PrestaShop store's category listing: every card carries the brand,
/// name, address, price and old price, so no request per product (271 products
/// are three pages of 100).
/// </summary>
/// <remarks>
/// <b>THE SESSION COMES FIRST.</b> The store sends a German IP (our server) from
/// /en/ to /de/ with a 302, and the currency parameter sent on that first request
/// was lost with the redirect: prices came back in EUR (measured). The first
/// request only picks up the session cookie; with it, /en/ answers in English
/// and the currency choice sticks. So: session, currency, then the pages. The
/// client doesn't follow redirects: a listing page answering 302 means the
/// session was lost, which must fail loudly rather than be read as a German page.
///
/// <b>The currency is checked on every page</b> against the market's, from the
/// page's own settings object: pounds shown as dollars (or the reverse) would look
/// plausible and be wrong.
///
/// <b>Stock is unknown.</b> The cards carry no stock signal (271 of 271 alike);
/// only the product pages do, and reading them would be 271 requests a round.
/// InStock stays null: no badge, rather than a guess.
/// </remarks>
public sealed partial class PrestaShopStoreScraper(
    HttpClient httpClient, PrestaShopStore store, ILogger<PrestaShopStoreScraper> logger) : IBrandScraper
{
    public const string HttpClientName = "prestashop-store";

    private const int PageSize = 100;
    private const int MaxPages = 20;
    private const decimal MinimumPrice = 1m;
    private static readonly TimeSpan PageDelay = TimeSpan.FromSeconds(1);

    public string BrandName => store.BrandName;
    public string BaseUrl => store.BaseUrl;

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var listUrl = store.BaseUrl.TrimEnd('/') + store.CategoryPath;

        // Session: from our server this is the 302 to /de/; only its cookie is needed.
        using (var session = await httpClient.GetAsync(listUrl, cancellationToken))
        {
            if (!session.IsSuccessStatusCode && !IsRedirect(session.StatusCode))
                session.EnsureSuccessStatusCode();
        }

        using (var currency = await httpClient.GetAsync(
            $"{listUrl}?SubmitCurrency=1&id_currency={store.CurrencyId}", cancellationToken))
        {
            currency.EnsureSuccessStatusCode();
        }

        var seller = SellerFromBaseUrl(store.BaseUrl);
        var result = new List<ScrapedProduct>();
        var withoutBrand = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            var html = await httpClient.GetStringAsync($"{listUrl}?resultsPerPage={PageSize}&page={page}", cancellationToken);

            var pageCurrency = PageCurrency(html);
            if (!string.Equals(pageCurrency, store.StoreMarket.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{store.BrandName}: the store answered in {pageCurrency ?? "an unknown currency"}, " +
                    $"{store.StoreMarket.Currency} expected.");
            }

            var cards = ParseCards(html);
            foreach (var card in cards)
            {
                if (card.Brand.Length == 0)
                {
                    withoutBrand++;
                    continue;
                }

                if (ToScrapedProduct(card, store, seller) is { } product)
                    result.Add(product);
            }

            if (cards.Count < PageSize)
                break;

            await Task.Delay(PageDelay, cancellationToken);
        }

        // A retailer's card without a brand would be filed under the retailer's
        // own name, a brand that doesn't exist. None were missing when measured.
        if (withoutBrand > 0)
            logger.LogWarning("{Store}: {Count} cards without a brand were skipped.", store.BrandName, withoutBrand);

        return result;
    }

    /// <summary>One product card from a category listing.</summary>
    internal sealed record Card(string Brand, string Name, string Url, decimal Price, decimal? RegularPrice, string? ImageUrl);

    internal static List<Card> ParseCards(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = new List<Card>();
        var nodes = doc.DocumentNode.SelectNodes("//article[contains(concat(' ', normalize-space(@class), ' '), ' product-miniature ')]");
        if (nodes is null)
            return cards;

        foreach (var node in nodes)
        {
            var title = node.SelectSingleNode(".//*[contains(@class,'product-title')]//a[@href]");
            var price = node.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' product-price ')]");
            if (title is null || price is null)
                continue;

            if (!decimal.TryParse(price.GetAttributeValue("content", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                continue;

            var brand = node.SelectSingleNode(".//div[contains(@class,'brand_name')]");
            var regular = node.SelectSingleNode(".//span[contains(@class,'regular-price')]");
            // The img src is a lazy-load SVG placeholder, the same on every card;
            // the real image is in the data attributes.
            var image = node.SelectSingleNode(".//img[@data-full-size-image-url]")?.GetAttributeValue("data-full-size-image-url", "")
                ?? node.SelectSingleNode(".//img[@data-src]")?.GetAttributeValue("data-src", "");

            cards.Add(new Card(
                Brand: Clean(brand?.InnerText),
                Name: Clean(title.InnerText),
                Url: WebUtility.HtmlDecode(title.GetAttributeValue("href", "")).Trim(),
                Price: amount,
                RegularPrice: regular is null ? null : ParseDisplayedPrice(regular.InnerText),
                ImageUrl: string.IsNullOrWhiteSpace(image) ? null : WebUtility.HtmlDecode(image).Trim()));
        }

        return cards;
    }

    internal static ScrapedProduct? ToScrapedProduct(Card card, PrestaShopStore store, string seller)
    {
        if (card.Name.Length == 0 || card.Price < MinimumPrice || NonSupplementProductFilter.IsAccessoryOrApparel(card.Name))
            return null;

        var category = ProductAttributeParser.InferCategory(card.Name, card.Brand)
            ?? ProductAttributeParser.InferCategory(card.Name);

        return new ScrapedProduct(
            Name: card.Name,
            Url: WithCurrency(card.Url, store.CurrencyId),
            ImageUrl: card.ImageUrl,
            Category: category,
            Price: card.Price,
            StoreOldPrice: card.RegularPrice > card.Price ? card.RegularPrice : null,
            ServingsPerPackage: ProductAttributeParser.ExtractServings(card.Name),
            BrandName: card.Brand,
            Seller: seller);
    }

    /// <summary>
    /// The product address with the store's own currency parameter: a visitor
    /// following it lands on the page in the currency we listed (measured: the
    /// same £3.44 / $4.39 as the listing, no redirect), not in the store's EUR.
    /// </summary>
    internal static string WithCurrency(string url, int currencyId) =>
        $"{url}{(url.Contains('?') ? '&' : '?')}SubmitCurrency=1&id_currency={currencyId}";

    /// <summary>The currency the page is priced in, from PrestaShop's settings object.</summary>
    internal static string? PageCurrency(string html) =>
        PageCurrencyRegex().Match(html) is { Success: true } m ? m.Groups["code"].Value : null;

    /// <summary>A displayed price such as "£2.39" or "$1,234.50" (English store pages).</summary>
    internal static decimal? ParseDisplayedPrice(string text)
    {
        var digits = PriceDigitsRegex().Match(WebUtility.HtmlDecode(text));
        if (!digits.Success)
            return null;

        return decimal.TryParse(digits.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string Clean(string? text) =>
        WhitespaceRegex().Replace(WebUtility.HtmlDecode(text ?? ""), " ").Trim();

    private static bool IsRedirect(HttpStatusCode code) => (int)code is >= 300 and < 400;

    private static string SellerFromBaseUrl(string baseUrl)
    {
        var host = new Uri(baseUrl).Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    // "currency":{"id":2,"name":"United Kingdom Pounds","iso_code":"GBP",...} inside
    // "var prestashop = {...}". The switcher's list of every currency is HTML-encoded
    // (&quot;), so it can't match here.
    [GeneratedRegex(@"""currency"":\{""id"":\d+,""name"":""[^""]*"",""iso_code"":""(?<code>[A-Z]{3})""")]
    private static partial Regex PageCurrencyRegex();

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex PriceDigitsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
