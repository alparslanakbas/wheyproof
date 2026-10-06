using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Images;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        // The gap rule (2026-10-06): eight days at $41.50, then six days unseen, then three days at $35.26.
        // The old calculation kept $41.50 as the usual price (a 15% "real drop"); after the break there is
        // no usual price yet, so no drop.
        var returned = Add(naked, "Returned After Gap", "protein-powder", null,
            [.. Enumerable.Repeat(41.50m, 8), 35.26m, 35.26m, 35.26m]);
        foreach (var p in returned.PriceHistories.Where(p => p.Price == 41.50m))
            p.ScrapedAt = p.ScrapedAt.AddDays(-5);
        // Control: a two-day break is under the threshold (one missed run at a daily source); the drop stays.
        var shortGap = Add(naked, "Short Gap Drop", "protein-powder", null, Week(100m, 90m));
        foreach (var p in shortGap.PriceHistories.Where(p => p.Price == 100m))
            p.ScrapedAt = p.ScrapedAt.AddDays(-1);

        // Same name, same price, two retailers: the sort keys are identical.
        for (var i = 0; i < 4; i++)
        {
            Add(optimum, "Gold Standard 100% Whey 5 lb", "protein-powder", "amazon.com", Week(80m, 75m));
            Add(optimum, "Gold Standard 100% Whey 5 lb", "protein-powder", "bodybuilding.com", Week(80m, 75m));
        }
        Add(vivo, "Vivo Life Perform", null, null, 55m);
        Add(vivo, "Vivo Life Magnesium", "vitamins", "vivolife.com", Week(18m, 14m));

        VisibleProducts = n;
        DiscountedProducts = 8 + 1 + 8 + 1 + 1; // wheys, D3, Gold Standards, magnesium, short gap drop

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

    // A product back after a break longer than the threshold earns its usual price again; a short break
    // (a missed run) must not erase a drop.
    [DatabaseFact]
    public async Task A_product_back_after_a_gap_gets_its_usual_price_from_after_the_gap()
    {
        await using var db = data.Context();

        var result = await Get(Service(db), null, null, null, false, 1, 100);

        var returned = Assert.Single(result.Items, d => d.ProductName == "Returned After Gap");
        Assert.Equal(0m, returned.DiscountPercent);
        Assert.Equal(35.26m, returned.ReferencePrice);
        var shortGap = Assert.Single(result.Items, d => d.ProductName == "Short Gap Drop");
        Assert.Equal(100m, shortGap.ReferencePrice);
    }

    // Favorites take the latest price from one batch query rather than the projection (2026-10-06; the
    // projection's FirstOrDefault windowed the whole price history). Each product must match its product page.
    [DatabaseFact]
    public async Task Favorites_give_every_product_the_same_latest_price_as_its_product_page()
    {
        await using var db = data.Context();
        var service = Service(db);
        var ids = await db.Products.Select(p => p.Id).ToListAsync();

        var favorites = await service.GetDealsByIdsAsync(ids);

        Assert.True(favorites.Count >= data.VisibleProducts, $"{favorites.Count} products returned");
        foreach (var f in favorites)
        {
            var product = await service.GetProductByIdAsync(f.ProductId);
            Assert.NotNull(product);
            Assert.Equal((product.CurrentPrice, product.ScrapedAt, product.ReferencePrice, product.DiscountPercent),
                (f.CurrentPrice, f.ScrapedAt, f.ReferencePrice, f.DiscountPercent));
        }
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
        Assert.Equal(8 + 1 + 1, (await catalog.GetBrandStatsAsync("Naked Nutrition")).DiscountCount); // wheys, D3, short gap drop (not the returned one)
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

    // The admin subscriber list (2026-10-04): page, status filter and email search run
    // in the query. The test adds its own subscribers with a "list-" prefix so other
    // tests' rows don't change the counts.
    [DatabaseFact]
    public async Task Subscriber_list_pages_filters_and_searches()
    {
        await using var db = data.Context();
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 12; i++)
        {
            // i % 3: 0 active, 1 pending, 2 unsubscribed; four of each.
            db.Subscribers.Add(new Subscriber
            {
                Email = $"list-{i:00}@example.test",
                Token = Guid.NewGuid().ToString("N"),
                IsConfirmed = i % 3 == 0,
                SubscribedAt = now.AddMinutes(-i),
                UnsubscribedAt = i % 3 == 2 ? now : null,
            });
        }
        await db.SaveChangesAsync();
        var service = Subscribers(db);

        var first = await service.ListForAdminAsync("list-", null, 1, 10);
        var second = await service.ListForAdminAsync("list-", null, 2, 10);
        Assert.Equal(12, first.Total);
        Assert.Equal(10, first.Subscribers.Count);
        Assert.Equal(2, second.Subscribers.Count);
        Assert.Equal("list-01@example.test", first.Subscribers[0].Email); // newest first
        Assert.Equal(12, first.Subscribers.Concat(second.Subscribers).Select(s => s.Id).Distinct().Count());

        foreach (var status in new[] { SubscriberStatus.Active, SubscriberStatus.Pending, SubscriberStatus.Unsubscribed })
        {
            var filtered = await service.ListForAdminAsync("list-", status, 1, 50);
            Assert.Equal(4, filtered.Total);
            Assert.All(filtered.Subscribers, s => Assert.Equal(status, s.Status));
        }

        // An uppercase search must match too.
        var single = await service.ListForAdminAsync("LIST-07", null, 1, 50);
        Assert.Equal("list-07@example.test", Assert.Single(single.Subscribers).Email);
    }

    // Permanent delete: the watchlist and favorites go through the cascade, another
    // subscriber's rows stay. The constraint lives in the database (migration), not in code.
    [DatabaseFact]
    public async Task Deleting_a_subscriber_removes_their_watches_and_favorites()
    {
        await using var db = data.Context();
        var product = await db.Products.OrderBy(p => p.Id).FirstAsync();
        Subscriber New(string email) => new()
        {
            Email = email, Token = Guid.NewGuid().ToString("N"), IsConfirmed = true, SubscribedAt = DateTimeOffset.UtcNow,
        };
        var troll = New("delete-troll@example.test");
        var real = New("delete-real@example.test");
        db.Subscribers.AddRange(troll, real);
        await db.SaveChangesAsync();
        db.ProductWatches.AddRange(
            new ProductWatch { SubscriberId = troll.Id, ProductId = product.Id, CreatedAt = DateTimeOffset.UtcNow },
            new ProductWatch { SubscriberId = real.Id, ProductId = product.Id, CreatedAt = DateTimeOffset.UtcNow });
        db.ProductFavorites.Add(new ProductFavorite { SubscriberId = troll.Id, ProductId = product.Id, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = Subscribers(db);
        Assert.True(await service.DeleteAsync(troll.Id));
        Assert.False(await service.DeleteAsync(troll.Id)); // the second time: gone

        await using var check = data.Context();
        Assert.False(await check.Subscribers.AnyAsync(s => s.Id == troll.Id));
        Assert.False(await check.ProductWatches.IgnoreQueryFilters().AnyAsync(w => w.SubscriberId == troll.Id));
        Assert.False(await check.ProductFavorites.IgnoreQueryFilters().AnyAsync(f => f.SubscriberId == troll.Id));
        Assert.True(await check.ProductWatches.IgnoreQueryFilters().AnyAsync(w => w.SubscriberId == real.Id));
    }

    private static SubscriberService Subscribers(AppDbContext db) =>
        new(db, new NoEmail(), new ConfigurationBuilder().Build(), NullLogger<SubscriberService>.Instance);

    // Listing and deleting send no email; if they do, the test should catch it.
    private sealed class NoEmail : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test must not send email.");
    }
}
