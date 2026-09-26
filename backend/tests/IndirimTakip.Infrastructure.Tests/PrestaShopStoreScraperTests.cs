using System.Net;
using System.Text;
using IndirimTakip.Infrastructure.Scraping.PrestaShop;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// PrestaShop category listing (365Rider). The markup below follows the store's
/// real cards as measured on 2026-09-26: the brand in div.brand_name, the price in
/// span.product-price's content attribute, the old price as display text, and a
/// lazy-load SVG placeholder in img src with the real image in data attributes.
/// </summary>
public class PrestaShopStoreScraperTests
{
    private static readonly PrestaShopStore UkStore =
        new("365Rider", "https://www.365rider.com", "/en/93-nutrition", CurrencyId: 2, Market: IndirimTakip.Infrastructure.SiteMarket.Uk);

    private static string Card(int id, string brand, string name, string price, string? regular = null) => $"""
        <div class="js-product-miniature-wrapper js-product-miniature-wrapper-{id} col-6">
        <article
        class="product-miniature product-miniature-default product-miniature-grid js-product-miniature"
        data-id-product="{id}"
        data-id-product-attribute="0"
        >
        <div class="thumbnail-container">
          <a href="https://www.365rider.com/en/nutrition/{id}-item.html" class="thumbnail product-thumbnail">
            <img
            data-src="https://www.365rider.com/{id}-home_default/item.jpg"
            src="data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%3E%3C/svg%3E"
            alt="{name}"
            data-full-size-image-url="https://www.365rider.com/{id}-thickbox_default/item.jpg"
            class="img-fluid js-lazy-product-image">
          </a>
        </div>
        <div class="product-description">
          <div class="product-category-name text-muted">Nutrition</div>
          <div class="brand_name">
          {brand}
          </div>
          <h2 class="h3 product-title">
            <a href="https://www.365rider.com/en/nutrition/{id}-item.html">{name}</a>
          </h2>
          <div class="product-price-and-shipping">
            <a href="https://www.365rider.com/en/nutrition/{id}-item.html"> <span  class="product-price" content="{price}" aria-label="Price">
            £{price}
            </span></a>
            {(regular is null ? "" : $"<span class=\"regular-price text-muted\">£{regular}</span>")}
          </div>
        </div>
        </article>
        </div>
        """;

    private static string Page(string currency, params string[] cards) => $$"""
        <html lang="en"><head>
        <script>var prestashop = {"cart":{"products":[]},"currency":{"id":2,"name":"United Kingdom Pounds","iso_code":"{{currency}}","iso_code_num":"826","sign":"£"},"customer":null};</script>
        </head><body>
        <div data-languages="[{&quot;id_currency&quot;:8,&quot;iso_code&quot;:&quot;USD&quot;}]"></div>
        {{string.Join("\n", cards)}}
        </body></html>
        """;

    [Fact]
    public void Cards_are_read_from_the_listing()
    {
        var html = Page("GBP",
            Card(5289, "Maurten", "Maurten Gel100 - 25g", "3.44"),
            Card(21966, "Nutrinovex", "Nutrinovex Cramp Rescuer Energy Gel", "1.91", regular: "2.39"));

        var cards = PrestaShopStoreScraper.ParseCards(html);

        Assert.Equal(2, cards.Count);
        Assert.Equal("Maurten", cards[0].Brand);
        Assert.Equal("Maurten Gel100 - 25g", cards[0].Name);
        Assert.Equal("https://www.365rider.com/en/nutrition/5289-item.html", cards[0].Url);
        Assert.Equal(3.44m, cards[0].Price);
        Assert.Null(cards[0].RegularPrice);
        Assert.Equal(2.39m, cards[1].RegularPrice);
    }

    /// <summary>
    /// The img src is the same SVG placeholder on every card; taking it would put
    /// one blank picture on the whole catalog.
    /// </summary>
    [Fact]
    public void Image_comes_from_the_data_attribute_not_the_placeholder()
    {
        var card = PrestaShopStoreScraper.ParseCards(Page("GBP", Card(5289, "Maurten", "Maurten Gel100", "3.44"))).Single();

        Assert.Equal("https://www.365rider.com/5289-thickbox_default/item.jpg", card.ImageUrl);
    }

    [Fact]
    public void Product_keeps_its_brand_the_retailer_is_the_seller_and_the_link_carries_the_currency()
    {
        var card = PrestaShopStoreScraper.ParseCards(Page("GBP",
            Card(21966, "Nutrinovex", "Nutrinovex Cramp Rescuer Energy Gel", "1.91", regular: "2.39"))).Single();

        var product = PrestaShopStoreScraper.ToScrapedProduct(card, UkStore, "365rider.com");

        Assert.NotNull(product);
        Assert.Equal("Nutrinovex", product.BrandName);
        Assert.Equal("365rider.com", product.Seller);
        Assert.Equal(2.39m, product.StoreOldPrice);
        Assert.Null(product.InStock); // the cards carry no stock signal
        Assert.Equal("https://www.365rider.com/en/nutrition/21966-item.html?SubmitCurrency=1&id_currency=2", product.Url);
    }

    [Fact]
    public void Old_price_not_above_the_price_is_dropped()
    {
        var card = new PrestaShopStoreScraper.Card("SiS", "SiS Go Energy Gel", "https://x.test/1.html", 2.50m, 2.50m, null);

        Assert.Null(PrestaShopStoreScraper.ToScrapedProduct(card, UkStore, "365rider.com")!.StoreOldPrice);
    }

    [Theory]
    [InlineData("SiS Go Energy Gel", 0.96)]          // below the price floor
    [InlineData("Maurten Water Bottle 500ml", 9.95)] // accessory
    public void Floor_and_accessory_rows_are_skipped(string name, double price)
    {
        var card = new PrestaShopStoreScraper.Card("Maurten", name, "https://x.test/1.html", (decimal)price, null, null);

        Assert.Null(PrestaShopStoreScraper.ToScrapedProduct(card, UkStore, "365rider.com"));
    }

    /// <summary>
    /// The page's own settings object decides the currency. The switcher lists
    /// every currency, HTML-encoded; it must not be read as the page's.
    /// </summary>
    [Fact]
    public void Page_currency_comes_from_the_settings_object()
    {
        Assert.Equal("GBP", PrestaShopStoreScraper.PageCurrency(Page("GBP")));
        Assert.Null(PrestaShopStoreScraper.PageCurrency(
            "<div data-languages=\"[{&quot;id_currency&quot;:8,&quot;iso_code&quot;:&quot;USD&quot;}]\"></div>"));
    }

    [Theory]
    [InlineData("£2.39", "2.39")]
    [InlineData("€2.50", "2.50")]
    [InlineData("  $1,234.50 ", "1234.50")]
    public void Displayed_price_is_parsed(string text, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PrestaShopStoreScraper.ParseDisplayedPrice(text));
    }

    [Fact]
    public void Displayed_price_without_digits_is_null()
    {
        Assert.Null(PrestaShopStoreScraper.ParseDisplayedPrice("Price on request"));
    }

    /// <summary>
    /// The measured order: the first visit only gets the session (from a German IP
    /// it is a 302 to /de/), then the currency, then the pages. Sent on the first,
    /// redirected request, the currency was lost and prices came back in EUR.
    /// </summary>
    [Fact]
    public async Task Session_then_currency_then_pages()
    {
        var handler = new StubHandler(Page("GBP", Card(5289, "Maurten", "Maurten Gel100 - 25g", "3.44")));
        var scraper = new PrestaShopStoreScraper(new HttpClient(handler), UkStore, NullLogger<PrestaShopStoreScraper>.Instance);

        var products = await scraper.ScrapeAsync();

        Assert.Single(products);
        Assert.Equal(
            [
                "https://www.365rider.com/en/93-nutrition",
                "https://www.365rider.com/en/93-nutrition?SubmitCurrency=1&id_currency=2",
                "https://www.365rider.com/en/93-nutrition?resultsPerPage=100&page=1",
            ],
            handler.Requested);
    }

    /// <summary>EUR on a page the UK edition reads would look plausible and be wrong.</summary>
    [Fact]
    public async Task A_page_in_another_currency_fails_loudly()
    {
        var handler = new StubHandler(Page("EUR", Card(5289, "Maurten", "Maurten Gel100 - 25g", "3.60")));
        var scraper = new PrestaShopStoreScraper(new HttpClient(handler), UkStore, NullLogger<PrestaShopStoreScraper>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scraper.ScrapeAsync());

        Assert.Contains("answered in EUR, GBP expected", error.Message);
    }

    /// <summary>A listing page answering 302 means the session was lost.</summary>
    [Fact]
    public async Task A_redirected_listing_page_fails()
    {
        var handler = new StubHandler(page: null);
        var scraper = new PrestaShopStoreScraper(new HttpClient(handler), UkStore, NullLogger<PrestaShopStoreScraper>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => scraper.ScrapeAsync());
    }

    private sealed class StubHandler(string? page) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            HttpResponseMessage response;
            if (url.EndsWith("/93-nutrition", StringComparison.Ordinal))
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://www.365rider.com/de/93-ernahrung");
            }
            else if (url.Contains("SubmitCurrency", StringComparison.Ordinal) || page is not null)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(page ?? "ok", Encoding.UTF8, "text/html"),
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://www.365rider.com/de/93-ernahrung?page=1");
            }

            return Task.FromResult(response);
        }
    }
}
