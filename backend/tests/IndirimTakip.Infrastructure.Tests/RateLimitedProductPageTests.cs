using System.Net;
using IndirimTakip.Core.Entities;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Its own database (TEST_DB's name + "_ratelimit"): DealsListDatabase drops and
/// recreates TEST_DB itself, and sharing it would race.
/// </summary>
public sealed class RateLimitDatabase : IAsyncLifetime
{
    public string? ConnectionString { get; } = Derive(Environment.GetEnvironmentVariable("TEST_DB"));

    private static string? Derive(string? testDb)
    {
        if (string.IsNullOrEmpty(testDb))
            return null;
        var builder = new NpgsqlConnectionStringBuilder(testDb);
        builder.Database += "_ratelimit";
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
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// A store that answers 429 gets no more product-page requests in that run
/// (measured 2026-10-18: the detail backfill sent ~30 requests in a row to Quest,
/// all 429, each retried through the home tunnel), while other stores carry on.
/// </summary>
public class RateLimitedProductPageTests(RateLimitDatabase database) : IClassFixture<RateLimitDatabase>
{
    [DatabaseFact]
    public async Task Detail_backfill_leaves_a_store_after_its_first_429()
    {
        await using var db = database.Context();
        var limited = new Brand { Name = "Limited Store", BaseUrl = "https://limited.example" };
        var calm = new Brand { Name = "Calm Store", BaseUrl = "https://calm.example" };
        db.Brands.AddRange(limited, calm);
        for (var i = 0; i < 3; i++)
            db.Products.Add(NewProduct(limited, $"https://limited.example/products/p{i}"));
        for (var i = 0; i < 2; i++)
            db.Products.Add(NewProduct(calm, $"https://calm.example/products/p{i}"));
        await db.SaveChangesAsync();

        var limitedFetcher = new FakeFetcher("Limited Store", HttpStatusCode.TooManyRequests);
        var calmFetcher = new FakeFetcher("Calm Store", null);
        var service = new ProductDetailBackfillService(
            db, [limitedFetcher, calmFetcher], NullLogger<ProductDetailBackfillService>.Instance, TimeSpan.Zero);

        await service.BackfillAsync();

        Assert.Equal(1, limitedFetcher.Calls);
        Assert.Equal(2, calmFetcher.Calls);
        var stamps = await db.Products.AsNoTracking()
            .Where(p => p.BrandId == limited.Id || p.BrandId == calm.Id)
            .Select(p => new { p.BrandId, p.NutritionCheckedAt })
            .ToListAsync();
        // The refused store's products stay unstamped, so they come first next run.
        Assert.All(stamps.Where(s => s.BrandId == limited.Id), s => Assert.Null(s.NutritionCheckedAt));
        Assert.All(stamps.Where(s => s.BrandId == calm.Id), s => Assert.NotNull(s.NutritionCheckedAt));
    }

    [DatabaseFact]
    public async Task Rating_refresh_skips_a_store_after_its_first_429_and_stamps_everything()
    {
        await using var db = database.Context();
        var brand = new Brand { Name = "Rating Brand", BaseUrl = "https://a.example" };
        db.Brands.Add(brand);
        // Interleaved, so the refused store's later products come after the 429.
        var urls = new[]
        {
            "https://a.example/products/1", "https://b.example/products/1", "https://a.example/products/2",
            "https://b.example/products/2", "https://a.example/products/3",
        };
        // Never checked, so they head the queue whatever ran before.
        var products = urls.Select(u => NewProduct(brand, u)).ToList();
        products.ForEach(p => p.RatingCheckedAt = null);
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        var handler = new CountingHandler(host => host == "a.example" ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
        var service = new ProductRatingRefreshService(
            db, new FakeHttpClientFactory(handler), NullLogger<ProductRatingRefreshService>.Instance, TimeSpan.Zero);

        // Only these five are unchecked; the backfill test's products were stamped as checked.
        await service.RefreshAsync(maxProducts: urls.Length);

        Assert.Equal(1, handler.Requests("a.example"));
        Assert.Equal(2, handler.Requests("b.example"));
        var ids = products.Select(p => p.Id).ToList();
        var stamped = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.RatingCheckedAt != null).CountAsync();
        Assert.Equal(urls.Length, stamped);
    }

    // RatingCheckedAt is set so these products stay out of the rating test's queue.
    private static Product NewProduct(Brand brand, string url) =>
        new() { Brand = brand, Name = url, Url = url, RatingCheckedAt = DateTimeOffset.UtcNow.AddDays(-1) };

    private sealed class FakeFetcher(string brandName, HttpStatusCode? failWith) : IBrandScraper, IProductDetailFetcher
    {
        public int Calls { get; private set; }
        public string BrandName => brandName;
        public string BaseUrl => "https://example";

        public Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScrapedProduct>>([]);

        public Task<ProductDetails> FetchDetailsAsync(string productUrl, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (failWith is { } status)
                throw new HttpRequestException($"Response status code does not indicate success: {(int)status}.", null, status);
            return Task.FromResult(new ProductDetails(null, null, null));
        }
    }

    private sealed class CountingHandler(Func<string, HttpStatusCode> answer) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> requests = [];

        public int Requests(string host) => requests.GetValueOrDefault(host);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            requests[host] = Requests(host) + 1;
            return Task.FromResult(new HttpResponseMessage(answer(host)) { Content = new StringContent("<html></html>") });
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
