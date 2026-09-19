using IndirimTakip.Infrastructure.Scraping.Shopify;
using IndirimTakip.Infrastructure.Scraping.Woo;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Tests;

// The US site and the UK section are one application run twice; Market:Code is
// what tells the two processes apart.
public class SiteMarketTests
{
    private static SiteMarket Read(string? code) =>
        SiteMarket.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Market:Code"] = code })
            .Build());

    // The US site existed before the setting: without it, nothing may change.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData(" us ")]
    public void Missing_or_us_is_the_us_site(string? code) =>
        Assert.Same(SiteMarket.Us, Read(code));

    [Theory]
    [InlineData("UK")]
    [InlineData("uk")]
    [InlineData("GB")]
    public void Uk_and_its_iso_code_are_the_uk_section(string code) =>
        Assert.Same(SiteMarket.Uk, Read(code));

    // A guessed market would scrape the wrong stores into the wrong database.
    [Fact]
    public void An_unknown_code_stops_the_start_up() =>
        Assert.Throws<InvalidOperationException>(() => Read("DE"));

    [Fact]
    public void Each_market_formats_prices_in_its_own_currency()
    {
        Assert.Equal("$39.99", 39.99m.ToString("C2", SiteMarket.Us.Culture));
        Assert.Equal("£39.99", 39.99m.ToString("C2", SiteMarket.Uk.Culture));
    }

    // Every store belongs to exactly one market: the two lists split the
    // registry, they never share a store (a shared one would be scraped into
    // both databases, in one of them in the wrong currency).
    [Fact]
    public void Market_store_lists_split_the_registry()
    {
        var us = ShopifyStores.ForMarket(SiteMarket.Us).ToList();
        var uk = ShopifyStores.ForMarket(SiteMarket.Uk).ToList();

        Assert.Equal(ShopifyStores.All.Count, us.Count + uk.Count);
        Assert.Empty(us.Intersect(uk));
        Assert.All(us, s => Assert.Same(SiteMarket.Us, s.StoreMarket));

        Assert.Equal(WooStores.All.Count,
            WooStores.ForMarket(SiteMarket.Us).Count() + WooStores.ForMarket(SiteMarket.Uk).Count());
    }

    // Naked lists its UK editions as market-uk. The US site drops them; the UK
    // section keeps them and drops the US ones instead.
    [Theory]
    [InlineData(new[] { "market-uk" }, false)]
    [InlineData(new[] { "market-gb" }, false)]
    [InlineData(new[] { "market-eu", "market-uk" }, false)]
    [InlineData(new[] { "market-us" }, true)]
    [InlineData(new[] { "market-eu" }, true)]
    [InlineData(new string[0], false)]
    public void The_uk_section_reads_market_tags_the_other_way_round(string[] tags, bool dropped)
    {
        Assert.Equal(dropped, ShopifyStoreScraper.IsOtherMarketOnly(tags, SiteMarket.Uk));
    }

    [Fact]
    public void A_uk_store_answering_in_dollars_is_refused()
    {
        var store = new WooStore("Form", "https://formnutrition.com", Market: SiteMarket.Uk);

        Assert.Throws<InvalidOperationException>(
            () => WooStoreScraper.ToScrapedProducts(WooProduct("USD"), store).ToList());
        Assert.Equal(39.00m, Assert.Single(WooStoreScraper.ToScrapedProducts(WooProduct("GBP"), store)).Price);
    }

    private static WooProduct WooProduct(string currency) => new()
    {
        Id = 1,
        Name = "Performance Protein",
        Permalink = "https://formnutrition.com/p",
        Type = "simple",
        IsInStock = true,
        IsPurchasable = true,
        Prices = new WooPrices { Price = "3900", RegularPrice = "3900", CurrencyCode = currency, CurrencyMinorUnit = 2 },
        Categories = [],
        Attributes = [],
        Variations = [],
    };
}
