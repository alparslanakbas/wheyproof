namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Gives outgoing requests the bot's identity with <see cref="WebBotAuth"/> (its User-Agent, and the
/// signature when signing is on), optionally only for some hosts.
/// </summary>
/// <remarks>
/// The rating client visits every store, so there it marks Shopify hosts only: other stores keep the
/// browser User-Agent some of them require, and the registration's User-Agent stays the one Shopify sees.
/// </remarks>
public sealed class WebBotAuthHandler(WebBotAuth auth, IReadOnlySet<string>? onlyHosts = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && (onlyHosts is null || onlyHosts.Contains(uri.Host)))
            auth.Sign(request);
        return base.SendAsync(request, cancellationToken);
    }
}
