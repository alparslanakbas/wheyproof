using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// A test that runs only when <c>TEST_DB</c> is set. Locally, when it isn't,
/// it shows as "skipped" (it doesn't silently pass). In CI it is NOT skipped:
/// if the variable goes missing there the test runs and fails, otherwise the
/// gate would turn green without ever running it (a test that counts as passed
/// but measures nothing is worse than no test).
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_DB"))
            && Environment.GetEnvironmentVariable("CI") != "true")
            Skip = "TEST_DB isn't set; the real database test was skipped.";
    }
}

/// <summary>
/// Migrates an empty test database and fills it with a small catalog full of
/// traps. Does NOTHING unless the database name contains "test": the database
/// is dropped and recreated at the start, and a wrong connection string would
/// take real data with it.
/// </summary>
public sealed class DealsListDatabase : IAsyncLifetime
{
    public string? ConnectionString { get; } = Environment.GetEnvironmentVariable("TEST_DB");

    /// <summary>Products the unfiltered list must show.</summary>
    public int VisibleProducts { get; private set; }

    /// <summary>Products with a real discount (latest price below the 30-day reference).</summary>
    public int DiscountedProducts { get; private set; }

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

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var naked = new Brand { Name = "Naked Nutrition", BaseUrl = "https://www.nakednutrition.com" };
        var optimum = new Brand { Name = "Optimum Nutrition", BaseUrl = "https://www.optimumnutrition.com" };
        var vivo = new Brand { Name = "Vivo Life", BaseUrl = "https://www.vivolife.com" };
        var hiddenBrand = new Brand { Name = "Hidden Brand", BaseUrl = "https://hidden.example", IsActive = false };
        db.Brands.AddRange(naked, optimum, vivo, hiddenBrand);

        var n = 0;
        Product Add(Brand brand, string name, string? category, string? seller, params decimal[] prices)
        {
            n++;
            var product = new Product
            {
                Brand = brand,
                Name = name,
                Url = $"https://source.example/{n}",
                Category = category,
                Seller = seller,
            };
            // Prices oldest to newest; the last one today, the others a day apart.
            for (var i = 0; i < prices.Length; i++)
            {
                product.PriceHistories.Add(new PriceHistory
                {
                    Price = prices[i],
                    ScrapedAt = now.AddDays(-(prices.Length - 1 - i)).AddMinutes(-1),
                });
            }
            db.Products.Add(product);
            return product;
        }

        // A real discount: the earlier price held for at least a week (the usual
        // price), then a drop.
        static decimal[] Week(decimal earlier, decimal latest) =>
            [.. Enumerable.Repeat(earlier, PriceSummaryRefresher.UsualPriceDays), latest];

        // Visible products. Deliberately tricky: the same name on two sellers
        // (without a tie-breaker the order shifts between pages), mixed case.
        for (var i = 1; i <= 8; i++)
            Add(naked, $"Naked Whey {i} lb", "protein-powder", null, Week(60m + i, 50m + i));
        for (var i = 1; i <= 5; i++)
            Add(naked, $"Naked Creatine {i}", "creatine", null, 30m);
        Add(naked, "VITAMIN D3 5000 IU", "vitamins", null, Week(20m, 15m));
        Add(naked, "Vitamin C", "vitamins", null, 12m);
        Add(naked, "Naked EAA", "amino-acids", null, 25m, 28m);

        // Visible but NOT discounted (the 2026-10-03 rule): the earlier price
        // was seen for less than a week. The old calculation (the 30-day high)
        // counted both as discounted; the spiked one would have shown 75%.
        Add(naked, "Short History Drop", "creatine", null, 30m, 25m);
        Add(naked, "Spiked Creatine", "creatine", null,
            [.. Enumerable.Repeat(50m, 8), 200m, 200m, 50m]);

        // Same name, same price, two retailers: the sort keys are identical.
        for (var i = 0; i < 4; i++)
        {
            Add(optimum, "Gold Standard 100% Whey 5 lb", "protein-powder", "amazon.com", Week(80m, 75m));
            Add(optimum, "Gold Standard 100% Whey 5 lb", "protein-powder", "bodybuilding.com", Week(80m, 75m));
        }
        Add(vivo, "Vivo Life Perform", null, null, 55m);
        Add(vivo, "Vivo Life Magnesium", "vitamins", "vivolife.com", Week(18m, 14m));

        VisibleProducts = n;
        DiscountedProducts = 8 + 1 + 8 + 1; // wheys, D3, Gold Standards, magnesium

        // Products that must NOT show.
        var stale = Add(naked, "Stale Product", "protein-powder", null, 99m);
        foreach (var p in stale.PriceHistories)
            p.ScrapedAt = now.AddDays(-5);
        var inactive = Add(naked, "Inactive Product", "protein-powder", null, 99m);
        inactive.IsActive = false;
        Add(hiddenBrand, "Hidden Brand Product", "protein-powder", null, 99m);

        await db.SaveChangesAsync();
    }
}

/// <summary>
/// <see cref="DealsQueryService.GetDealsAsync"/> against real PostgreSQL
/// (security/architecture review, 2026-09-26, finding 11). This class took the
/// Turkish site down twice and both bugs were in the SQL translation, which
/// neither the build nor in-memory tests can see. The key check is paging:
/// walking every page must return each product EXACTLY once.
/// </summary>
public class DealsListDatabaseTests(DealsListDatabase data) : IClassFixture<DealsListDatabase>
{
    private static readonly string?[] Sorts =
        [null, "name_asc", "name_desc", "price_asc", "price_desc", "newest", "oldest"];

    private static readonly string[]?[] Sellers =
        [null, [DealsQueryService.BrandDirectSellerLabel], [DealsQueryService.DealerSellerLabel]];

    private static readonly string?[] Searches = [null, "whey", "vitamin", "VITAMIN", "naked whey", "gold"];

    private static DealsQueryService Service(AppDbContext db) =>
        new(db, Options.Create(new AffiliateOptions()), new ProductImageOptions());

    private static Task<PagedResult<DealDto>> Get(
        DealsQueryService service, string? sort, string[]? sellers, string? search,
        bool discounted, int page, int pageSize) =>
        service.GetDealsAsync(
            PriceSummaryRefresher.WindowDays, null, null, sellers, search, null, null,
            discounted, false, sort, page, pageSize);

    [DatabaseFact]
    public async Task Walking_every_page_returns_each_product_exactly_once()
    {
        await using var db = data.Context();
        var service = Service(db);
        var tried = 0;

        foreach (var sort in Sorts)
        foreach (var sellers in Sellers)
        foreach (var search in Searches)
        foreach (var discounted in new[] { false, true })
        {
            var first = await Get(service, sort, sellers, search, discounted, 1, 4);
            var seen = new List<int>();
            for (var page = 1; page <= Math.Max(first.TotalPages, 1); page++)
            {
                var result = page == 1 ? first : await Get(service, sort, sellers, search, discounted, page, 4);
                Assert.True(result.Items.Count <= 4);
                seen.AddRange(result.Items.Select(d => d.ProductId));
            }

            var state = $"sort={sort}, seller={sellers?[0]}, search={search}, discounted={discounted}";
            Assert.True(seen.Count == seen.Distinct().Count(), $"Repeated product: {state}");
            Assert.True(seen.Count == first.TotalCount, $"Missing product ({seen.Count}/{first.TotalCount}): {state}");

            // The same request must give the same order.
            var again = await Get(service, sort, sellers, search, discounted, 1, 4);
            Assert.Equal(first.Items.Select(d => d.ProductId), again.Items.Select(d => d.ProductId));
            tried++;
        }

        Assert.Equal(Sorts.Length * Sellers.Length * Searches.Length * 2, tried);
    }

    [DatabaseFact]
    public async Task Unfiltered_list_hides_stale_inactive_and_hidden_brand()
    {
        await using var db = data.Context();

        var result = await Get(Service(db), null, null, null, false, 1, 100);

        Assert.Equal(data.VisibleProducts, result.TotalCount);
        Assert.DoesNotContain(result.Items,
            d => d.ProductName is "Stale Product" or "Inactive Product" or "Hidden Brand Product");
    }

    [DatabaseFact]
    public async Task Discounted_filter_returns_only_real_discounts()
    {
        await using var db = data.Context();

        var result = await Get(Service(db), null, null, null, true, 1, 100);

        Assert.Equal(data.DiscountedProducts, result.TotalCount);
        Assert.All(result.Items, d => Assert.True(d.DiscountPercent > 0, d.ProductName));
        Assert.DoesNotContain(result.Items, d => d.ProductName is "Short History Drop" or "Spiked Creatine");
    }

    // A product with no usual price stays in the list with a 0 discount: the
    // reference is the current price. A NULL reference would have made the list
    // query drop the product entirely.
    [DatabaseFact]
    public async Task Short_history_product_stays_listed_with_zero_discount()
    {
        await using var db = data.Context();

        var result = await Get(Service(db), null, null, null, false, 1, 100);

        var shortHistory = Assert.Single(result.Items, d => d.ProductName == "Short History Drop");
        Assert.Equal(0m, shortHistory.DiscountPercent);
        Assert.Equal(shortHistory.CurrentPrice, shortHistory.ReferencePrice);
        var spiked = Assert.Single(result.Items, d => d.ProductName == "Spiked Creatine");
        Assert.Equal(50m, spiked.ReferencePrice);
    }

    // The product page, favorites and stats computed the reference themselves
    // (the window's high): on the Turkish site the first version moved only the
    // lists to the usual price and the product page kept showing the spiked
    // product's old percentage. The same product must show the same reference
    // everywhere.
    [DatabaseFact]
    public async Task Product_page_favorites_and_stats_use_the_list_reference()
    {
        await using var db = data.Context();
        var service = Service(db);
        var spiked = await db.Products.Where(p => p.Name == "Spiked Creatine").Select(p => p.Id).SingleAsync();
        var shortHistory = await db.Products.Where(p => p.Name == "Short History Drop").Select(p => p.Id).SingleAsync();

        var product = await service.GetProductByIdAsync(spiked);
        Assert.NotNull(product);
        Assert.Equal(50m, product.ReferencePrice);
        Assert.Equal(0m, product.DiscountPercent);

        var favorites = await service.GetDealsByIdsAsync([spiked, shortHistory]);
        Assert.Equal(2, favorites.Count);
        Assert.All(favorites, d => Assert.Equal(0m, d.DiscountPercent));

        var catalog = new CatalogStatsQueryService(db);
        Assert.Equal(data.DiscountedProducts, (await catalog.GetHomepageStatsAsync()).DiscountCount);
        Assert.Equal(8 + 1, (await catalog.GetBrandStatsAsync("Naked Nutrition")).DiscountCount); // wheys, D3
    }

    [DatabaseFact]
    public async Task Uppercase_search_matches_lowercase()
    {
        await using var db = data.Context();
        var service = Service(db);

        var upper = await Get(service, "name_asc", null, "VITAMIN", false, 1, 100);
        var lower = await Get(service, "name_asc", null, "vitamin", false, 1, 100);

        Assert.Equal(lower.Items.Select(d => d.ProductId), upper.Items.Select(d => d.ProductId));
        Assert.Contains(upper.Items, d => d.ProductName == "VITAMIN D3 5000 IU");
    }

    [DatabaseFact]
    public async Task Retailer_filter_returns_only_retailer_records()
    {
        await using var db = data.Context();

        var retailers = await Get(Service(db), null, [DealsQueryService.DealerSellerLabel], null, false, 1, 100);
        var own = await Get(Service(db), null, [DealsQueryService.BrandDirectSellerLabel], null, false, 1, 100);

        Assert.All(retailers.Items, d => Assert.NotNull(d.Seller));
        Assert.All(own.Items, d => Assert.Null(d.Seller));
        Assert.Equal(data.VisibleProducts, retailers.TotalCount + own.TotalCount);
    }
}
