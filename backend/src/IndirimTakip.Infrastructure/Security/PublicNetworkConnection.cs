using System.Net;
using System.Net.Sockets;

namespace IndirimTakip.Infrastructure.Security;

/// <summary>
/// An HTTP handler that only connects to public (internet) addresses.
/// </summary>
/// <remarks>
/// <b>WHY (security review, 2026-09-26).</b> The server requests addresses that
/// come from the sources: product images, product pages for ratings, detail
/// backfill, product URLs from site maps. We don't write those addresses; a
/// store's catalog or a leaked ingest key could turn them into INTERNAL ones
/// such as <c>http://169.254.169.254/</c> (the cloud provider's instance
/// metadata endpoint) or <c>http://db:5432</c>. The server would then become a
/// tool for scanning its own internal network (SSRF).
///
/// <b>THE CHECK HAPPENS AT CONNECT TIME.</b> Checking the URL before the request
/// wouldn't be enough: a name can resolve to a public IP first and to an
/// internal one at connect time (DNS rebinding), and a redirect (302) can point
/// inside. Here the decision looks at the IP the socket actually connects to,
/// and every new connection opened by a redirect goes through the same gate.
///
/// <b>ON EVERY CLIENT BY DEFAULT.</b> Applied to all clients in Infrastructure
/// through <c>ConfigureHttpClientDefaults</c>, so a new scraper can't forget
/// it. The one client that goes to an internal address on purpose (the output
/// cache warmup, <c>localhost</c>) picks its own handler explicitly.
/// </remarks>
public static class PublicNetworkConnection
{
    /// <summary>A new handler that connects to public addresses only.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        // With a proxy the socket would connect to the proxy and the check
        // would look at the proxy's address instead of the target. There is
        // no proxy on the server; off on purpose.
        UseProxy = false,
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var ip)
            ? [ip]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        // In a mixed answer (one public, one internal) only the public ones are
        // tried; an internal address is never a connection candidate.
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"'{host}' doesn't resolve to a public address; not connecting.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Is the address reachable from the internet? Loopback, private networks,
    /// link-local (including the instance metadata endpoint), CGNAT, multicast
    /// and reserved ranges are rejected.
    /// </summary>
    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0                                  // "this network"
                || b[0] == 10                                   // private
                || b[0] == 127                                  // loopback
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
                || (b[0] == 169 && b[1] == 254)                 // link-local, metadata endpoint
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // private (incl. Docker networks)
                || (b[0] == 192 && b[1] == 168)                 // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // IETF reserved
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // benchmarking
                || b[0] >= 224);                                // multicast, reserved, broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(ip.Equals(IPAddress.IPv6Any)
                || ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6UniqueLocal
                || ip.IsIPv6Multicast);
        }

        return false;
    }
}
