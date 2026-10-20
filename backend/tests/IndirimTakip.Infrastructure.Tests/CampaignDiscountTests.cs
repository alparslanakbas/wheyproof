using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Its own database (TEST_DB's name + "_campaign"): DealsListDatabase drops and recreates TEST_DB itself, and the
/// season-page products would also change that fixture's counts.
/// </summary>
public sealed class CampaignDatabase : IAsyncLifetime
{
    public string? ConnectionString { get; } = Derive(Environment.GetEnvironmentVariable("TEST_DB"));

    private static string? Derive(string? testDb)
    {
        if (string.IsNullOrEmpty(testDb))
            return null;
        var builder = new NpgsqlConnectionStringBuilder(testDb);
        builder.Database += "_campaign";
        return builder.ConnectionString;
    }

    public AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(ConnectionString))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("TEST_DB isn't set in CI; the database tests can't run.");
            return;
        }

        var name = new NpgsqlConnectionStringBuilder(ConnectionString).Database ?? string.Empty;
        if (!name.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"TEST_DB points at '{name}'; the name must contain 'test'.");

        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
        await new PriceSummaryRefresher(db, NullLogger<PriceSummaryRefresher>.Instance).RefreshAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // The Turkish site's cases (UrunListesiVeritabaniTests, 2026-10-07): every product shows a crossed-out
    // "was" price except the last one.
    private static async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var brand = new Brand { Name = "Campaign Brand", BaseUrl = "https://campaign.example" };
        db.Brands.Add(brand);

        var n = 0;
        void Add(string name, decimal? wasPrice, params decimal[] prices)
        {
            n++;
            var product = new Product { Brand = brand, Name = name, Url = $"https://campaign.example/{n}", Category = "mass-gainer" };
            // Oldest to newest; the last one today, the others a day apart.
            for (var i = 0; i < prices.Length; i++)
            {
                product.PriceHistories.Add(new PriceHistory
                {
                    Price = prices[i],
                    StoreOldPrice = wasPrice,
                    ScrapedAt = now.AddDays(-(prices.Length - 1 - i)).AddMinutes(-1),
                });
            }
            db.Products.Add(product);
        }

        Add("Campaign Real", 1000m, [.. Enumerable.Repeat(1000m, 10), 850m, 850m]);
        // A one-day 500 reading must not count as the reference (lowest seen on two days: 1000).
        Add("Campaign One-Day Error", 1100m, [.. Enumerable.Repeat(1000m, 5), 500m, .. Enumerable.Repeat(1000m, 4), 950m, 950m]);
        Add("Campaign Permanent", 1200m, [.. Enumerable.Repeat(800m, 35)]);
        Add("Campaign Not Cheaper", 1200m, [.. Enumerable.Repeat(700m, 10), 900m, 900m, 900m]);
        // The store's own sale ended: 300 for ten days, now 900 "down from 1500". A threefold gap, but not a data
        // error (a real 8-day sale on the Turkish site looked like this): it sold for less within the month.
        Add("Campaign Back From Sale", 1500m, [.. Enumerable.Repeat(300m, 10), 900m, 900m, 900m]);
        Add("Campaign Short History", 900m, 600m, 600m, 600m);
        Add("Campaign No Was Price", null, 500m, 500m);

        await db.SaveChangesAsync();
    }
}

public class CampaignDiscountTests(CampaignDatabase database) : IClassFixture<CampaignDatabase>
{
    [DatabaseFact]
    public async Task Classifies_against_the_lowest_price_in_the_30_days_before_the_discount()
    {
        await using var db = database.Context();
        var deals = new DealsQueryService(db, Options.Create(new AffiliateOptions()), new ProductImageOptions());

        var summary = await new CampaignDiscountService(db, deals).SummaryAsync();

        Assert.Equal(6, summary.StoreSaysSale);
        Assert.Equal((2, 2, 1, 1), (summary.Real, summary.NotCheaper, summary.Permanent, summary.InsufficientData));
        Assert.Equal(
            [("Campaign Real", 1000m, 15.0m), ("Campaign One-Day Error", 1000m, 5.0m)],
            summary.RealDiscounts.Select(d => (d.Product.ProductName, d.PriorLowest, d.RealPercent)));
    }
}
