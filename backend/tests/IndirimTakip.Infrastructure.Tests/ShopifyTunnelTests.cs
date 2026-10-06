using System.Net;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// A Shopify 429 is repeated through the home tunnel (see ShopifyTunnel). The key guarantee:
/// with the tunnel off or failing, the scraper sees exactly what it saw before the tunnel.
/// </summary>
public class ShopifyTunnelTests
{
    private const string Url = "https://nutricost.com/products.json?limit=250&page=1&currency=USD&country=US";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Records requests and answers with the given function.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static (HttpMessageInvoker Client, FakeHandler Direct, FakeHandler? Tunnel, FakeTime Time, ShopifyTunnel State)
        Build(HttpStatusCode directStatus, Func<HttpRequestMessage, HttpResponseMessage>? tunnelAnswer, bool withTunnel = true)
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero));
        var direct = new FakeHandler(_ => new HttpResponseMessage(directStatus));
        var tunnel = withTunnel ? new FakeHandler(tunnelAnswer ?? (_ => new HttpResponseMessage(HttpStatusCode.OK))) : null;
        var state = new ShopifyTunnel(tunnel, time);
        var handler = new ShopifyTunnelHandler(state, NullLogger<ShopifyTunnelHandler>.Instance) { InnerHandler = direct };
        return (new HttpMessageInvoker(handler), direct, tunnel, time, state);
    }

    private static Task<HttpResponseMessage> Send(HttpMessageInvoker client, string url = Url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Test/1.0");
        return client.SendAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task With_the_tunnel_off_a_429_reaches_the_scraper_unchanged()
    {
        var (client, direct, _, _, state) = Build(HttpStatusCode.TooManyRequests, null, withTunnel: false);

        var response = await Send(client);

        Assert.False(state.Enabled);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(direct.Requests);
    }

    [Fact]
    public async Task A_successful_direct_request_does_not_touch_the_tunnel()
    {
        var (client, direct, tunnel, _, state) = Build(HttpStatusCode.OK, null);

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(direct.Requests);
        Assert.Empty(tunnel!.Requests);
        Assert.False(state.UseTunnel);
    }

    [Fact]
    public async Task A_direct_429_is_repeated_through_the_tunnel_with_the_same_request()
    {
        var (client, direct, tunnel, _, _) = Build(HttpStatusCode.TooManyRequests, null);

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(direct.Requests);
        var tunnelled = Assert.Single(tunnel!.Requests);
        Assert.Equal(Url, tunnelled.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, tunnelled.Method);
        // Headers must be copied: some stores reject requests without a User-Agent.
        Assert.Equal("Test/1.0", tunnelled.Headers.UserAgent.ToString());
        // A request object can't be sent twice; the tunnel gets a copy.
        Assert.NotSame(direct.Requests[0], tunnelled);
    }

    [Fact]
    public async Task After_a_block_direct_is_skipped_for_the_sticky_period_then_tried_again()
    {
        var (client, direct, tunnel, time, _) = Build(HttpStatusCode.TooManyRequests, null);

        await Send(client);                           // direct 429 -> tunnel
        time.Now += TimeSpan.FromMinutes(29);
        await Send(client);                           // sticky: tunnel only

        Assert.Single(direct.Requests);
        Assert.Equal(2, tunnel!.Requests.Count);

        time.Now += TimeSpan.FromMinutes(2);          // minute 31: the period is over
        await Send(client);                           // direct first (429), then the tunnel

        Assert.Equal(2, direct.Requests.Count);
        Assert.Equal(3, tunnel.Requests.Count);
    }

    [Fact]
    public async Task When_the_tunnel_fails_the_direct_429_is_returned_and_direct_is_tried_first_again()
    {
        var (client, direct, tunnel, _, state) = Build(HttpStatusCode.TooManyRequests,
            _ => throw new HttpRequestException("Connection refused"));

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.False(state.UseTunnel);

        await Send(client);
        Assert.Equal(2, direct.Requests.Count);
        Assert.Equal(2, tunnel!.Requests.Count);
    }

    [Fact]
    public async Task If_the_tunnel_fails_during_the_sticky_period_the_request_goes_direct()
    {
        var (client, direct, _, _, state) = Build(HttpStatusCode.OK,
            _ => throw new HttpRequestException("Connection refused"));
        state.BlockSeen();

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(direct.Requests);
    }

    // 2026-10-05 18:34 (UK cycle): 10 of 10 stores answered 403 through the tunnel and sticky mode sent
    // every request to the home address. Now, if the home address is refused too, the scraper sees the
    // 429 it saw before the tunnel.
    [Fact]
    public async Task If_the_tunnel_is_refused_too_the_direct_429_is_returned_and_sticky_mode_ends()
    {
        var (client, _, tunnel, _, state) = Build(HttpStatusCode.TooManyRequests, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(tunnel!.Requests);
        Assert.False(state.UseTunnel);
        Assert.True(state.Usable); // one refusal doesn't close the tunnel
    }

    [Fact]
    public async Task Three_different_stores_refusing_rests_the_tunnel_for_two_hours()
    {
        var (client, _, tunnel, time, state) = Build(HttpStatusCode.TooManyRequests, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        foreach (var store in new[] { "a.com", "b.com", "c.com" })
            await Send(client, $"https://{store}/products.json");
        Assert.False(state.Usable);

        await Send(client, "https://d.com/products.json");
        Assert.Equal(3, tunnel!.Requests.Count); // nothing goes to the home address while it rests

        time.Now += ShopifyTunnel.ClosedFor + TimeSpan.FromMinutes(1);
        await Send(client, "https://d.com/products.json");
        Assert.Equal(4, tunnel.Requests.Count); // tried again once the period is over
    }

    [Fact]
    public async Task The_same_store_refusing_twice_counts_once()
    {
        var (client, _, _, _, state) = Build(HttpStatusCode.TooManyRequests, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        await Send(client, "https://a.com/products.json");
        await Send(client, "https://a.com/products.json");
        await Send(client, "https://b.com/products.json");

        Assert.True(state.Usable);
    }

    // A single store that blocks Turkey mustn't close the tunnel for everyone: a success resets the count.
    [Fact]
    public async Task A_successful_tunnel_answer_resets_the_rejection_count()
    {
        var (client, _, _, _, state) = Build(HttpStatusCode.TooManyRequests,
            request => new HttpResponseMessage(request.RequestUri!.Host == "ok.com" ? HttpStatusCode.OK : HttpStatusCode.Forbidden));

        foreach (var store in new[] { "a.com", "b.com", "ok.com", "c.com", "d.com" })
            await Send(client, $"https://{store}/products.json");

        Assert.True(state.Usable);
    }

    [Fact]
    public async Task If_the_tunnel_is_refused_during_the_sticky_period_the_request_goes_direct()
    {
        var (client, direct, _, _, state) = Build(HttpStatusCode.OK, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        state.BlockSeen();

        var response = await Send(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(direct.Requests);
        Assert.False(state.UseTunnel);
    }

    [Fact]
    public async Task A_tunnel_timeout_is_a_tunnel_failure_but_the_callers_cancellation_propagates()
    {
        // A connect timeout (TaskCanceledException without a cancellation request) = tunnel failed.
        var (client, _, _, _, _) = Build(HttpStatusCode.TooManyRequests, _ => throw new TaskCanceledException());
        var response = await Send(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        // A cancelled cycle (deploy, shutdown) must not be swallowed.
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url), cancel.Token));
    }

    [Theory]
    [InlineData("http://172.17.0.1:8888")]
    [InlineData(" http://172.17.0.1:8888/ ")]
    public void A_valid_proxy_address_is_accepted(string address)
    {
        var uri = ShopifyTunnel.ParseProxyAddress(address);
        Assert.Equal("172.17.0.1", uri.Host);
        Assert.Equal(8888, uri.Port);
    }

    [Theory]
    [InlineData("172.17.0.1:8888")]               // no scheme
    [InlineData("https://172.17.0.1:8888")]       // the proxy is reached without TLS
    [InlineData("http://172.17.0.1:8888/path")]
    [InlineData("http://172.17.0.1:8888/?a=1")]
    [InlineData("proxy")]
    public void A_bad_proxy_address_does_not_quietly_turn_the_tunnel_off(string address)
    {
        Assert.Throws<InvalidOperationException>(() => ShopifyTunnel.ParseProxyAddress(address));
    }

    [Fact]
    public void An_empty_setting_turns_the_tunnel_off()
    {
        Assert.False(ShopifyTunnel.Create(null, TimeProvider.System).Enabled);
        Assert.False(ShopifyTunnel.Create(" ", TimeProvider.System).Enabled);
    }

    [Fact]
    public async Task The_tunnel_client_connects_to_its_proxy_only()
    {
        var proxy = new Uri("http://172.17.0.1:8888");

        Assert.True(ShopifyTunnel.IsProxy(new DnsEndPoint("172.17.0.1", 8888), proxy));
        Assert.False(ShopifyTunnel.IsProxy(new DnsEndPoint("172.17.0.1", 8080), proxy));
        Assert.False(ShopifyTunnel.IsProxy(new DnsEndPoint("localhost", 8888), proxy));
        Assert.False(ShopifyTunnel.IsProxy(new DnsEndPoint("nutricost.com", 443), proxy));

        // Refused before any network access (WebProxy sends localhost around the proxy; that path
        // goes through this gate).
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ShopifyTunnel.ConnectToProxyAsync(new DnsEndPoint("localhost", 80), proxy, CancellationToken.None).AsTask());

        using var handler = ShopifyTunnel.CreateHandler(proxy);
        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    // The production address must be valid; a bad one would fail every Shopify crawl.
    [Fact]
    public void The_production_tunnel_address_is_valid()
    {
        var settings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Production.json")
            .Build();

        using var tunnel = ShopifyTunnel.Create(settings["Shopify:Tunnel"], TimeProvider.System);

        Assert.True(tunnel.Enabled);
    }

    // The tunnel belongs to the Shopify client only: other sources' 429s are real rate limits that
    // must not be bypassed from a home connection. A missing registration (AddTransient forgotten)
    // makes CreateHandler throw here.
    [Theory]
    [InlineData("US")]
    [InlineData("UK")]
    public void Only_the_Shopify_client_carries_the_tunnel_handler(string market)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Market:Code"] = market })
            .Build());
        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpMessageHandlerFactory>();

        var names = sp.GetServices<IConfigureOptions<HttpClientFactoryOptions>>()
            .OfType<ConfigureNamedOptions<HttpClientFactoryOptions>>()
            .Select(o => o.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct();

        var tunnelled = new List<string>();
        foreach (var name in names)
        {
            for (var handler = factory.CreateHandler(name!); handler is DelegatingHandler wrapper; handler = wrapper.InnerHandler!)
            {
                if (wrapper is ShopifyTunnelHandler)
                {
                    tunnelled.Add(name!);
                    break;
                }
            }
        }

        Assert.Equal(new[] { ShopifyStoreScraper.HttpClientName }, tunnelled.ToArray());
    }
}
