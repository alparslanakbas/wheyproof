using IndirimTakip.Core.Caching;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.OutputCaching;

namespace IndirimTakip.Api.Caching;

/// <summary>
/// After a scrape, evicts the public data cache by tag, then calls the most
/// requested endpoints once to fill it again.
///
/// <b>WHY IT WAS NEEDED (measured on the Turkish site).</b> The output cache
/// lived 60 seconds and traffic was low, so in practice EVERY visitor hit a
/// cold cache: <c>/api/deals</c> took 0.26 s from cache and <b>2.1 s</b> on a
/// miss; the home page (SSR calls these endpoints) took <b>6.0 s</b> cold and
/// 0.26 s warm.
///
/// 97.7% of that time was spent inside PostgreSQL (COUNT 654 ms + data query
/// 1,437 ms), 49 ms in C#. So the REAL fix was the query itself: storing the
/// per-product discount fields during the scrape (PriceSummaryRefresher). This
/// class keeps visitors off a cold cache on top of that.
///
/// <b>A longer lifetime alone wouldn't do:</b> long lifetime means stale data.
/// Tagged eviction lets the cache live long but drops it the moment a scrape
/// finishes, so the data is never older than one scrape.
/// </summary>
public sealed class OutputCacheRefresher(
    IOutputCacheStore cacheStore,
    IHttpClientFactory httpClientFactory,
    IServer server,
    IConfiguration configuration,
    ILogger<OutputCacheRefresher> logger) : IPublicCacheRefresher
{
    /// <summary>Tag given to the public data policy in Program.cs.</summary>
    public const string Tag = "public-data";

    /// <summary>
    /// Paths to warm: the endpoints the home page's SSR calls.
    ///
    /// The policy uses <c>SetVaryByQuery("*")</c>, so every distinct query
    /// string is a SEPARATE cache entry. These paths must match EXACTLY what
    /// the page requests; otherwise warming fills a different entry and the
    /// visitor still hits a cold cache.
    /// </summary>
    private static readonly string[] WarmupPaths =
    [
        "/api/deals?page=1&pageSize=24",
        "/api/stats",
        "/api/filters",
        "/api/brand-category-pairs",
        "/api/brand-product-counts",
        "/api/preferred-products?take=12",
    ];

    /// <summary>
    /// The address the application ITSELF listens on. Trusting the configured
    /// port is fragile: locally the launch profile uses 5156, the container uses
    /// PORT=8080, and either may change. The server's own address list is right
    /// in every environment; configuration only exists to override it by hand.
    /// </summary>
    private string? OwnBaseAddress()
    {
        var manual = configuration.GetValue<string>("OutputCache:WarmupBaseUrl");
        if (!string.IsNullOrWhiteSpace(manual))
            return manual.TrimEnd('/');

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                      ?? addresses?.FirstOrDefault();
        if (address is null)
            return null;

        // Wildcard addresses such as "http://+:8080" / "http://[::]:8080" can't
        // be used by a client; they are mapped to localhost.
        return address.Replace("://+", "://localhost")
                      .Replace("://[::]", "://localhost")
                      .Replace("://0.0.0.0", "://localhost")
                      .TrimEnd('/');
    }

    /// <summary>
    /// <b>THE CACHE KEY INCLUDES THE HOST AND THE SCHEME.</b> The first version
    /// warmed <c>http://localhost:8080</c>; the requests returned 200 and the log
    /// said "6/6 endpoints warmed", yet REAL visitors still hit a cold cache.
    /// Found by measuring in production: the same path with three Host headers:
    ///
    ///   Host: backend:8080          -> 0.001 s  (Age 81)
    ///   Host: localhost:8080        -> 0.003 s  (Age 84)
    ///   Host: the public API host   -> 2.123 s  (Age 0)
    ///
    /// So warming filled a key nobody used. The scheme is part of the key too:
    /// real requests arrive from Caddy with <c>X-Forwarded-Proto: https</c>, so
    /// <c>Request.Scheme</c> is "https".
    ///
    /// The request still goes from INSIDE the machine (it never leaves through
    /// Cloudflare); only the Host and scheme are set to match real traffic.
    /// Without configuration, warming fills the local key, which is right in
    /// development; production must set <c>OutputCache__WarmupHost</c>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await cacheStore.EvictByTagAsync(Tag, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not evict the cache; warming will still be attempted.");
        }

        // It sends HTTP requests to itself: the output cache stores HTTP
        // RESPONSES, so calling the query service directly wouldn't fill it.
        var baseAddress = OwnBaseAddress();
        if (baseAddress is null)
        {
            logger.LogWarning("Server address not found; cache warming skipped.");
            return;
        }

        var client = httpClientFactory.CreateClient(nameof(OutputCacheRefresher));
        var succeeded = 0;

        // Headers that make the warmed entry land on the same key as a real
        // visitor's; see the remarks above.
        var publicHost = configuration.GetValue<string>("OutputCache:WarmupHost");
        var publicScheme = configuration.GetValue("OutputCache:WarmupScheme", "https");

        foreach (var path in WarmupPaths)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseAddress + path);
                if (!string.IsNullOrWhiteSpace(publicHost))
                {
                    request.Headers.Host = publicHost;
                    request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", publicScheme);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    succeeded++;
                else
                    logger.LogWarning("Cache warming returned {Status} for {Path}.", (int)response.StatusCode, path);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // A failed warm-up must not affect the scrape; at worst the first
                // visitor sees the old (slow) behavior.
                logger.LogWarning(ex, "Cache warming failed for {Path}.", path);
            }
        }

        logger.LogInformation(
            "Public data cache refreshed: {Succeeded}/{Total} endpoints warmed.",
            succeeded, WarmupPaths.Length);
    }
}
