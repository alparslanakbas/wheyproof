using System.Net;
using System.Net.Sockets;
using IndirimTakip.Infrastructure.Security;

namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>
/// A fallback route that sends Shopify requests through a home connection (WireGuard tunnel).
/// </summary>
/// <remarks>
/// <b>WHY (2026-10-05).</b> Since 2026-09-29 Shopify answers the server's (data center) address
/// with 429 across the whole platform at certain hours, even for stores we never scrape: on
/// 2026-10-03 to 10-05 the block started in the afternoon and lifted between 04:00 and 06:00 UTC,
/// and night cycles lost 26 of 33 US stores and 10 of 13 UK ones. A home connection gets 200 in
/// the same minute. Slowing the rating refresh did not help, and it is not the trigger: on
/// 2026-10-05 the block began before that day's refreshes touched Shopify.
///
/// <b>Direct first, the tunnel on a 429.</b> The tunnel is used only while the block is on, so
/// the home line carries nothing during the day and a tunnel outage leaves daytime cycles
/// untouched. After a 429, requests skip the direct attempt for <see cref="StickyFor"/>: the
/// block lasts hours, and taking another 429 per store first would be a wasted request.
///
/// <b>The proxy.</b> tinyproxy on the host (172.17.0.1:8888, Docker bridge only, CONNECT to 443
/// only); its user is bound to wg0 with an "ip rule uidrange". Setting: <c>Shopify:Tunnel</c>;
/// empty means off (local development, tests) and clients behave as before.
///
/// <b>SSRF.</b> The tunnel client can't use <see cref="PublicNetworkConnection"/>: its socket
/// connects to the proxy, an internal address. Instead its connect callback allows the configured
/// proxy and nothing else (WebProxy sends some hosts, such as localhost, without the proxy; that
/// path is closed). The targets are the store addresses in code, not addresses from a source.
///
/// <b>Same prices? Measured (2026-10-05).</b> Through the tunnel (a Turkish address) all 41
/// Shopify stores returned their catalog; 3,659 of 3,661 prices equalled what the server had
/// fetched directly that day, and the other two were real price changes in between (Shopify's
/// updated_at). The currency check still guards every crawl.
/// </remarks>
public sealed class ShopifyTunnel : IDisposable
{
    internal static readonly TimeSpan StickyFor = TimeSpan.FromMinutes(30);

    private readonly HttpMessageInvoker? tunnel;
    private readonly TimeProvider time;

    // Until this moment (UTC ticks) requests go straight to the tunnel.
    private long tunnelUntil;

    public ShopifyTunnel(HttpMessageHandler? tunnelHandler, TimeProvider time)
    {
        tunnel = tunnelHandler is null ? null : new HttpMessageInvoker(tunnelHandler, disposeHandler: true);
        this.time = time;
    }

    /// <summary>Built from the configured proxy address; an empty address turns the tunnel off.</summary>
    public static ShopifyTunnel Create(string? proxyAddress, TimeProvider time) =>
        new(string.IsNullOrWhiteSpace(proxyAddress) ? null : CreateHandler(ParseProxyAddress(proxyAddress)), time);

    public bool Enabled => tunnel is not null;

    /// <summary>True while <see cref="StickyFor"/> hasn't passed since the last 429.</summary>
    public bool UseTunnel => Enabled && time.GetUtcNow().UtcTicks < Interlocked.Read(ref tunnelUntil);

    /// <summary>A direct request got a 429. True when the tunnel wasn't already in use (log once).</summary>
    public bool BlockSeen()
    {
        var isNew = !UseTunnel;
        Interlocked.Exchange(ref tunnelUntil, (time.GetUtcNow() + StickyFor).UtcTicks);
        return isNew;
    }

    /// <summary>The tunnel failed: try direct first again.</summary>
    public void Reset() => Interlocked.Exchange(ref tunnelUntil, 0);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        (tunnel ?? throw new InvalidOperationException("The Shopify tunnel is off.")).SendAsync(request, cancellationToken);

    public void Dispose() => tunnel?.Dispose();

    /// <summary>
    /// The proxy address must look like http://host:port. A bad value throws instead of quietly
    /// turning the tunnel off (a test checks the production setting).
    /// </summary>
    internal static Uri ParseProxyAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0)
            throw new InvalidOperationException($"Invalid Shopify:Tunnel '{address}' (expected http://host:port).");
        return uri;
    }

    internal static SocketsHttpHandler CreateHandler(Uri proxy) => new()
    {
        Proxy = new WebProxy(proxy),
        UseProxy = true,
        // Compression shrinks traffic on the home line about tenfold: a 250-product catalog page is
        // 115 KB instead of 1.2 MB (measured 2026-10-05).
        AutomaticDecompression = DecompressionMethods.All,
        // If the tunnel is down, don't hang; the scraper then sees the direct answer (the 429).
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = (context, cancellationToken) => ConnectToProxyAsync(context.DnsEndPoint, proxy, cancellationToken),
    };

    /// <summary>Connects to the proxy only; any other endpoint (a request sent around the proxy) is refused.</summary>
    internal static async ValueTask<Stream> ConnectToProxyAsync(DnsEndPoint endpoint, Uri proxy, CancellationToken cancellationToken)
    {
        if (!IsProxy(endpoint, proxy))
            throw new HttpRequestException($"The Shopify tunnel connects to its proxy only; '{endpoint.Host}:{endpoint.Port}' refused.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static bool IsProxy(DnsEndPoint endpoint, Uri proxy) =>
        string.Equals(endpoint.Host, proxy.IdnHost, StringComparison.OrdinalIgnoreCase) && endpoint.Port == proxy.Port;
}
