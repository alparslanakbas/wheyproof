namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Signs outgoing requests with <see cref="WebBotAuth"/>, optionally only for some hosts.
/// </summary>
/// <remarks>
/// On the Shopify client it sits OUTSIDE the tunnel handler, so a request repeated through the
/// home tunnel carries the same signature (it covers the host, which doesn't change). The rating
/// client visits every store, so there it signs Shopify hosts only: other stores keep the browser
/// User-Agent some of them require, and the registration's User-Agent stays the one Shopify sees.
/// </remarks>
public sealed class WebBotAuthHandler(WebBotAuth auth, IReadOnlySet<string>? onlyHosts = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (auth.Enabled && request.RequestUri is { } uri && (onlyHosts is null || onlyHosts.Contains(uri.Host)))
            auth.Sign(request);
        return base.SendAsync(request, cancellationToken);
    }
}
