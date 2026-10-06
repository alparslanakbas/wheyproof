using IndirimTakip.Api.Endpoints;
using IndirimTakip.Infrastructure.Deals;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// A product page renders the main list too, so the showcase and the card
/// sparklines ride along in the page's embedded transfer state. Both responses
/// were slimmed (2026-10-06); these tests check that nothing on screen changes.
/// </summary>
public class ProductPageTransferStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 2, 0, 0, TimeSpan.Zero);

    private static List<PricePointDto> Series(params decimal[] prices) =>
        prices.Select((p, i) => new PricePointDto(p, Start.AddHours(6 * i))).ToList();

    [Fact]
    public void An_equal_price_run_keeps_only_its_first_and_last_point()
    {
        var series = Series(29.99m, 29.99m, 29.99m, 29.99m, 29.99m);

        var result = PriceHistoryQueryService.RunEndpoints(series);

        Assert.Equal(new[] { series[0], series[4] }, result);
    }

    [Fact]
    public void A_price_change_keeps_both_runs_endpoints()
    {
        var series = Series(29.99m, 29.99m, 29.99m, 24.99m, 24.99m, 24.99m);

        var result = PriceHistoryQueryService.RunEndpoints(series);

        Assert.Equal(new[] { series[0], series[2], series[3], series[5] }, result);
    }

    [Fact]
    public void Distinct_consecutive_prices_and_short_series_stay_as_they_are()
    {
        var distinct = Series(1, 2, 3, 4);
        Assert.Equal(distinct, PriceHistoryQueryService.RunEndpoints(distinct));

        var single = Series(5);
        Assert.Equal(single, PriceHistoryQueryService.RunEndpoints(single));

        Assert.Empty(PriceHistoryQueryService.RunEndpoints([]));
    }

    // Proof the drawing is unchanged: every dropped point has the price of both
    // its kept neighbours, so it lies on the flat line joining them.
    [Fact]
    public void Every_dropped_point_has_the_price_of_both_kept_neighbours()
    {
        var prices = Enumerable.Repeat(39.99m, 30)
            .Append(34.99m)
            .Concat(Enumerable.Repeat(39.99m, 20))
            .Concat(Enumerable.Repeat(31.99m, 40))
            .Concat([33.49m, 32.99m])
            .Concat(Enumerable.Repeat(31.99m, 46))
            .ToArray();
        var series = Series(prices);

        var result = PriceHistoryQueryService.RunEndpoints(series);

        Assert.Equal(series[0], result[0]);
        Assert.Equal(series[^1], result[^1]);
        Assert.True(result.Count < series.Count / 10, $"{result.Count} of {series.Count} points left");
        foreach (var point in series.Except(result))
        {
            var before = result.Last(k => k.ScrapedAt < point.ScrapedAt);
            var after = result.First(k => k.ScrapedAt > point.ScrapedAt);
            Assert.Equal(point.Price, before.Price);
            Assert.Equal(point.Price, after.Price);
        }
    }

    [Fact]
    public void The_showcase_response_drops_description_and_nutrition_and_keeps_the_rest()
    {
        var deal = new DealDto(
            ProductId: 2076, ProductName: "Nutricost Creatine Monohydrate Capsules - 500 Capsules",
            ProductUrl: "https://example.com/p", ImageUrl: "/images/2076.webp", Category: "creatine",
            Size: "500 Capsules", Flavor: null, ServingSizeGrams: null, ServingsPerPackage: 166,
            Description: new string('a', 26_000), NutritionJson: "{\"protein\":0}",
            ProteinPerServingGrams: null, BrandName: "Nutricost", CurrentPrice: 29.95m,
            ReferencePrice: 34.95m, DiscountPercent: 14.3m, StoreOldPrice: null,
            StoreDiscountPercent: null, ScrapedAt: Start, IsAtThirtyDayLow: true,
            RatingValue: 4.7m, RatingCount: 1287);

        var showcase = DealsEndpoints.ForShowcase(deal);

        Assert.Null(showcase.Description);
        Assert.Null(showcase.NutritionJson);
        Assert.Equal(deal with { Description = null, NutritionJson = null }, showcase);
    }
}
