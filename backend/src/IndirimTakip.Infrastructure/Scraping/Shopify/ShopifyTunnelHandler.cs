using System.Net;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping.Shopify;

/// <summary>
/// In the Shopify client's handler chain: when a direct request gets a 429, the same request is
/// repeated through <see cref="ShopifyTunnel"/> (the reasons are there).
/// </summary>
/// <remarks>
/// If the tunnel fails, the scraper sees the direct answer (the 429), so the worst case is the
/// behavior from before the tunnel. In the HttpClient log a tunnelled request shows as an inner
/// "Received ... - 429" followed by an outer "End processing ... - 200".
/// </remarks>
public sealed class ShopifyTunnelHandler(ShopifyTunnel tunnel, ILogger<ShopifyTunnelHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A request with a body can't be copied; Shopify requests are GETs anyway. While the tunnel
        // rests because the home address was refused too (see ShopifyTunnel.RejectThreshold), direct only.
        if (!tunnel.Usable || request.Content is not null)
            return await base.SendAsync(request, cancellationToken);

        if (tunnel.UseTunnel)
            return await ThroughTunnelAsync(request, cancellationToken)
                ?? await base.SendAsync(request, cancellationToken);

        var direct = await base.SendAsync(request, cancellationToken);
        if (direct.StatusCode != HttpStatusCode.TooManyRequests)
            return direct;

        if (tunnel.BlockSeen())
            logger.LogInformation("Shopify {Host} answered 429; Shopify requests go through the home tunnel for {Minutes} minutes.",
                request.RequestUri?.Host, ShopifyTunnel.StickyFor.TotalMinutes);

        var tunnelled = await ThroughTunnelAsync(request, cancellationToken);
        if (tunnelled is null)
            return direct;

        direct.Dispose();
        return tunnelled;
    }

    /// <summary>
    /// The tunnel's response, or null when the tunnel failed or the home address was refused too
    /// (403/429): the caller falls back to direct, and the scraper sees what it saw before the tunnel
    /// (a 429 keeps 0 products rather than failing the store).
    /// </summary>
    private async Task<HttpResponseMessage?> ThroughTunnelAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await tunnel.SendAsync(Copy(request), cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            // Try direct first again; once the tunnel is back, the next 429 opens it again.
            tunnel.Reset();
            logger.LogWarning(ex, "The Shopify tunnel failed ({Host}); continuing with the direct answer.",
                request.RequestUri?.Host);
            return null;
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var store = request.RequestUri?.Host ?? "";
            logger.LogWarning("Shopify {Host} answered {Status} through the home tunnel too; continuing with the direct answer.",
                store, (int)response.StatusCode);
            if (tunnel.Rejected(store))
                logger.LogWarning("The home tunnel was refused by {Threshold} different stores in a row; it rests for {Hours} hours.",
                    ShopifyTunnel.RejectThreshold, ShopifyTunnel.ClosedFor.TotalHours);
            response.Dispose();
            return null;
        }

        tunnel.Succeeded();
        return response;
    }

    // A request can't be sent twice, so the tunnel gets a copy. Headers (User-Agent included)
    // arrive here after HttpClient has added its default headers.
    private static HttpRequestMessage Copy(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var (name, values) in request.Headers)
            copy.Headers.TryAddWithoutValidation(name, values);
        return copy;
    }
}
