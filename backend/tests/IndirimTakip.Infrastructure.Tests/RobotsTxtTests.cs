using System.Net;
using IndirimTakip.Infrastructure.Scraping;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>robots.txt compliance for Shopify requests (RFC 9309), the promise in the bot registration.</summary>
public class RobotsTxtTests
{
    private const string Token = RobotsTxtHandler.ProductToken;

    // The shape of Shopify's default robots.txt (abridged from a live store, 2026-10-08).
    private const string ShopifyDefault = """
        # we use Shopify as our ecommerce platform
        User-agent: *
        Disallow: /a/downloads/-/*
        Disallow: /admin
        Disallow: /cart
        Disallow: /checkout
        Disallow: /collections/*sort_by*
        Disallow: /search
        Disallow: /recommendations/products
        Sitemap: https://www.kaged.com/sitemap.xml

        User-agent: adsbot-google
        Disallow: /checkouts/
        Disallow: /checkout
        """;

    [Theory]
    [InlineData("/products.json?limit=250&page=1&currency=USD&country=US", true)]
    [InlineData("/?currency=USD&country=US", true)]
    [InlineData("/products/kaged-pre-workout", true)]
    [InlineData("/admin", false)]
    [InlineData("/checkout", false)]
    [InlineData("/collections/all?sort_by=price", false)]
    [InlineData("/robots.txt", true)]
    public void Shopify_default_rules_allow_our_paths(string path, bool allowed) =>
        Assert.Equal(allowed, RobotsRules.Parse(ShopifyDefault, Token).Allows(path));

    [Fact]
    public void A_group_naming_our_bot_replaces_the_star_group()
    {
        var rules = RobotsRules.Parse("User-agent: *\nAllow: /\n\nUser-agent: WheyProofBot\nDisallow: /products.json\n", Token);

        Assert.False(rules.Allows("/products.json?limit=250"));
        Assert.True(rules.Allows("/products/x"));
    }

    [Fact]
    public void Several_agents_can_share_one_group()
    {
        var rules = RobotsRules.Parse("User-agent: otherbot\nUser-agent: wheyproofbot\nDisallow: /\n", Token);

        Assert.False(rules.Allows("/products.json"));
    }

    [Theory]
    [InlineData("/products.json", true)]                  // the longer allow wins
    [InlineData("/products/x", false)]
    public void The_longest_matching_rule_wins(string path, bool allowed) =>
        Assert.Equal(allowed, RobotsRules.Parse("User-agent: *\nDisallow: /products\nAllow: /products.json\n", Token).Allows(path));

    [Fact]
    public void Allow_wins_a_tie() =>
        Assert.True(RobotsRules.Parse("User-agent: *\nDisallow: /p\nAllow: /p\n", Token).Allows("/p"));

    [Theory]
    [InlineData("/products.json", false)]
    [InlineData("/products.json?limit=1", true)]           // $ anchors the end; the query follows
    [InlineData("/x/y.json", false)]
    public void Star_and_dollar_patterns(string path, bool allowed) =>
        Assert.Equal(allowed, RobotsRules.Parse("User-agent: *\nDisallow: /*.json$\n", Token).Allows(path));

    [Fact]
    public void An_empty_disallow_allows_everything() =>
        Assert.True(RobotsRules.Parse("User-agent: *\nDisallow:\n", Token).Allows("/products.json"));

    // ---- the handler -----------------------------------------------------------------------------

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store(Func<string, HttpResponseMessage> robots) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(request.RequestUri.AbsolutePath == "/robots.txt"
                ? robots(request.Headers.UserAgent.ToString())
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpMessageInvoker Client, Store Store, FakeTime Time) Build(Func<string, HttpResponseMessage> robots,
        IReadOnlySet<string>? onlyHosts = null)
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.Zero));
        var store = new Store(robots);
        var handler = new RobotsTxtHandler(new RobotsTxtCache(time), NullLogger<RobotsTxtHandler>.Instance, onlyHosts)
        {
            InnerHandler = store,
        };
        return (new HttpMessageInvoker(handler), store, time);
    }

    private static HttpResponseMessage Robots(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private static Task<HttpResponseMessage> Get(HttpMessageInvoker client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TestAgent/1.0");
        return client.SendAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task An_allowed_request_goes_out_and_robots_txt_is_read_once_a_day()
    {
        var (client, store, time) = Build(_ => Robots(ShopifyDefault));

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "https://www.kaged.com/products.json?limit=250")).StatusCode);
        await Get(client, "https://www.kaged.com/products/x");
        time.Now += TimeSpan.FromHours(23);
        await Get(client, "https://www.kaged.com/products.json?page=2");

        Assert.Equal(["/robots.txt", "/products.json?limit=250", "/products/x", "/products.json?page=2"], store.Paths);

        time.Now += TimeSpan.FromHours(2);                 // past a day: read again
        await Get(client, "https://www.kaged.com/products/y");
        Assert.Equal(2, store.Paths.Count(p => p == "/robots.txt"));
    }

    [Fact]
    public async Task A_disallowed_request_is_not_sent()
    {
        var (client, store, _) = Build(_ => Robots("User-agent: wheyproofbot\nDisallow: /products.json\n"));

        var response = await Get(client, "https://www.kaged.com/products.json?limit=250");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RobotsTxtHandler.RefusedReason, response.ReasonPhrase);
        Assert.Equal(["/robots.txt"], store.Paths);
    }

    [Fact]
    public async Task Robots_txt_is_fetched_with_the_requests_user_agent()
    {
        string? seen = null;
        var (client, _, _) = Build(ua => { seen = ua; return Robots(ShopifyDefault); });

        await Get(client, "https://www.kaged.com/products.json");

        Assert.Equal("TestAgent/1.0", seen);
    }

    [Fact]
    public async Task A_missing_robots_txt_allows_everything()
    {
        var (client, store, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "https://www.kaged.com/products.json")).StatusCode);
        Assert.Contains("/products.json", store.Paths);
    }

    [Fact]
    public async Task Unreachable_robots_txt_skips_the_request_and_asks_again_an_hour_later()
    {
        var status = HttpStatusCode.TooManyRequests;
        var (client, store, time) = Build(_ => status == HttpStatusCode.OK ? Robots(ShopifyDefault) : new HttpResponseMessage(status));

        Assert.Equal(HttpStatusCode.Forbidden, (await Get(client, "https://www.kaged.com/products.json")).StatusCode);
        await Get(client, "https://www.kaged.com/products.json");
        Assert.Equal(["/robots.txt"], store.Paths);         // not asked before every request

        status = HttpStatusCode.OK;
        time.Now += RobotsTxtCache.RetryUnreachableAfter;
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "https://www.kaged.com/products.json")).StatusCode);
    }

    [Fact]
    public async Task Unreachable_robots_txt_keeps_the_last_rules()
    {
        var status = HttpStatusCode.OK;
        var (client, _, time) = Build(_ => status == HttpStatusCode.OK ? Robots(ShopifyDefault) : new HttpResponseMessage(status));

        await Get(client, "https://www.kaged.com/products.json");
        status = HttpStatusCode.ServiceUnavailable;
        time.Now += TimeSpan.FromHours(25);

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "https://www.kaged.com/products.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(client, "https://www.kaged.com/admin")).StatusCode);
    }

    [Fact]
    public async Task Hosts_outside_the_list_are_not_checked()
    {
        var (client, store, _) = Build(_ => Robots("User-agent: *\nDisallow: /\n"), new HashSet<string> { "www.kaged.com" });

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "https://www.myprotein.com/p/x")).StatusCode);
        Assert.DoesNotContain("/robots.txt", store.Paths);
    }
}
