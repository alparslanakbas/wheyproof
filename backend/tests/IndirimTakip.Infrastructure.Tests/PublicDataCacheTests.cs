using System.Collections.Concurrent;
using IndirimTakip.Api.Caching;
using IndirimTakip.Api.Endpoints;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// The public data cache key (security review, 26 Sept): a random query
/// parameter must not skip the cache, while a parameter the endpoint REALLY
/// reads must keep opening its own entry — if that breaks, a <c>page=2</c>
/// request gets <c>page=1</c>'s response. The policy runs on a real request
/// pipeline with the SAME setup as Program.cs (<see cref="PublicDataCache"/>).
/// </summary>
public class PublicDataCacheTests
{
    private sealed class Server(WebApplication app, ConcurrentDictionary<string, int> calls) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public int Calls(string endpoint) => calls.GetValueOrDefault(endpoint);

        public async Task GetAsync(string url) => (await Client.GetAsync(url)).EnsureSuccessStatusCode();

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private static async Task<Server> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOutputCache(o =>
        {
            o.AddBasePolicy(p => p.NoCache());
            o.AddPolicy("public", p => PublicDataCache.Apply(p, TimeSpan.FromMinutes(5)));
        });
        var app = builder.Build();
        app.UseOutputCache();

        var calls = new ConcurrentDictionary<string, int>();
        int Count(string endpoint) => calls.AddOrUpdate(endpoint, 1, (_, n) => n + 1);

        app.MapGet("/no-params", () => Count("no-params")).CacheOutput("public");
        app.MapGet("/list", (int? page, string[]? brands) => Count("list")).CacheOutput("public");
        app.MapGet("/product/{id:int}", (int id, int? days) => Count("product")).CacheOutput("public");
        app.MapGet("/raw", (HttpContext ctx) => Count("raw")).CacheOutput("public");

        await app.StartAsync();
        return new Server(app, calls);
    }

    [Fact]
    public async Task A_random_parameter_on_a_parameterless_endpoint_hits_the_same_entry()
    {
        await using var s = await StartAsync();

        await s.GetAsync("/no-params?r=1");
        await s.GetAsync("/no-params?r=2");
        await s.GetAsync("/no-params");

        Assert.Equal(1, s.Calls("no-params"));
    }

    [Fact]
    public async Task A_bound_parameter_opens_its_own_entry_an_unrelated_one_does_not()
    {
        await using var s = await StartAsync();

        await s.GetAsync("/list?page=1");
        await s.GetAsync("/list?page=2");
        Assert.Equal(2, s.Calls("list"));

        await s.GetAsync("/list?page=1&r=9");
        await s.GetAsync("/list?PAGE=2");
        Assert.Equal(2, s.Calls("list"));

        await s.GetAsync("/list?page=1&brands=a&brands=b");
        Assert.Equal(3, s.Calls("list"));
        await s.GetAsync("/list?brands=a&brands=b&page=1&utm_source=x");
        Assert.Equal(3, s.Calls("list"));
    }

    [Fact]
    public async Task A_route_value_stays_in_the_path_an_unrelated_query_parameter_is_dropped()
    {
        await using var s = await StartAsync();

        await s.GetAsync("/product/1?days=7");
        await s.GetAsync("/product/1?days=7&r=1");
        Assert.Equal(1, s.Calls("product"));

        await s.GetAsync("/product/2?days=7");
        await s.GetAsync("/product/1?days=30");
        Assert.Equal(3, s.Calls("product"));
    }

    [Fact]
    public async Task An_endpoint_reading_the_raw_request_keeps_the_old_behaviour()
    {
        // An endpoint taking HttpContext may read the query itself; narrowing
        // the key would hand different requests the same response. Every
        // parameter stays in the key here.
        await using var s = await StartAsync();

        await s.GetAsync("/raw?x=1");
        await s.GetAsync("/raw?x=2");

        Assert.Equal(2, s.Calls("raw"));
    }

    [Fact]
    public void Real_endpoint_keys_match_the_parameters_in_their_signatures()
    {
        // The site's real endpoint definitions are built; service types are
        // registered so their parameters are recognised as services (none is
        // resolved, no database needed). Building the endpoint list also runs
        // minimal API inference: an error thrown at exactly this stage took every
        // endpoint of the Turkish API down with 500s on 6 Sept.
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<DealsQueryService>();
        builder.Services.AddScoped<CatalogStatsQueryService>();
        builder.Services.AddScoped<PriceHistoryQueryService>();
        builder.Services.AddScoped<ArticleService>();
        builder.Services.AddScoped<CouponService>();
        var app = builder.Build();
        app.MapDealsEndpoints("public");
        app.MapCouponEndpoints("public");
        app.MapArticleEndpoints("public");
        app.MapPriceHistoryEndpoints("public");

        var actual = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => $"{e.RoutePattern.RawText} -> {string.Join(",", EndpointQueryKeysPolicy.KeysFor(e).ToArray())}")
            .Order(StringComparer.Ordinal)
            .ToList();

        const string list = "brands,categories,sellers,search,minPrice,maxPrice,sortBy,page,pageSize,expandSynonyms,preferBrandStore";
        string[] expected =
        [
            "/api/articles -> ",
            "/api/articles/{slug} -> ",
            "/api/best-value-brands -> category",
            "/api/best-value-per-serving -> category,brands,search,page,pageSize",
            "/api/brand-category-pairs -> ",
            "/api/brand-comparison -> brand1,brand2",
            "/api/brand-product-counts -> ",
            "/api/brand-stats -> brand,category",
            "/api/category-price-stats -> category",
            "/api/category-product-counts -> ",
            "/api/coupons -> ",
            $"/api/deals -> {list}",
            "/api/filters -> ",
            "/api/preferred-products -> count",
            $"/api/products -> {list}",
            "/api/products/sitemap -> ",
            "/api/products/sparklines -> ids,days",
            "/api/products/{id:int} -> ",
            "/api/products/{id:int}/price-history -> days",
            "/api/stats -> ",
            $"/api/store-deals -> {list}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }
}
