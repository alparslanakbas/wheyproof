using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Each host's robots.txt rules, read at most once a day (singleton, shared by the clients).
/// </summary>
/// <remarks>
/// Status handling follows RFC 9309 2.3.1: 2xx is parsed; 4xx other than 429 means no robots.txt,
/// so everything is allowed; 429, 5xx or no answer means "unreachable", and then the last rules we
/// had are kept. With none ever read, the request is skipped (the RFC's complete disallow) and
/// robots.txt is asked again an hour later, not before every request.
/// </remarks>
public sealed class RobotsTxtCache(TimeProvider time)
{
    internal static readonly TimeSpan FreshFor = TimeSpan.FromHours(24);
    internal static readonly TimeSpan RetryUnreachableAfter = TimeSpan.FromHours(1);

    private sealed record Entry(RobotsRules? Rules, DateTimeOffset NextFetch);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rules for <paramref name="host"/>, or null when they couldn't be read (skip the request).</summary>
    public async Task<RobotsRules?> GetAsync(string host, string productToken,
        Func<CancellationToken, Task<HttpResponseMessage>> fetch, ILogger logger, CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(host, out var known) && time.GetUtcNow() < known.NextFetch)
            return known.Rules;

        var gate = gates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (entries.TryGetValue(host, out known) && time.GetUtcNow() < known.NextFetch)
                return known.Rules;

            var previous = known?.Rules;
            RobotsRules? rules;
            var unreachable = false;
            try
            {
                using var response = await fetch(cancellationToken);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                    rules = RobotsRules.Parse(await response.Content.ReadAsStringAsync(cancellationToken), productToken);
                else if (status is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
                    rules = RobotsRules.AllowAll;
                else
                {
                    unreachable = true;
                    rules = previous;
                    logger.LogWarning("robots.txt of {Host} answered {Status}; {Action}.", host, status,
                        previous is null ? "requests to it are skipped for now" : "keeping the last rules");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                unreachable = true;
                rules = previous;
                logger.LogWarning(ex, "robots.txt of {Host} could not be read; {Action}.", host,
                    previous is null ? "requests to it are skipped for now" : "keeping the last rules");
            }

            entries[host] = new Entry(rules, time.GetUtcNow() + (unreachable ? RetryUnreachableAfter : FreshFor));
            return rules;
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>
/// Lets a request through only if the host's robots.txt allows it for our bot (see
/// <see cref="RobotsRules"/>). A refused request is not sent: it gets a local 403 whose reason says
/// why, so the scraper treats it as a failed source and the store goes stale rather than being read.
/// </summary>
/// <remarks>
/// Outermost on the Shopify client, so robots.txt itself is fetched through the rest of the chain:
/// signed if signing is on, and through the home tunnel when Shopify refuses the server.
/// </remarks>
public sealed class RobotsTxtHandler(RobotsTxtCache cache, ILogger<RobotsTxtHandler> logger, IReadOnlySet<string>? onlyHosts = null)
    : DelegatingHandler
{
    /// <summary>The name robots.txt files address us by (our User-Agent product token).</summary>
    public const string ProductToken = "wheyproofbot";

    internal const string RefusedReason = "Disallowed by robots.txt";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null || !uri.IsAbsoluteUri || uri.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
            || (onlyHosts is not null && !onlyHosts.Contains(uri.Host)))
            return await base.SendAsync(request, cancellationToken);

        var rules = await cache.GetAsync(uri.Host, ProductToken, ct =>
        {
            var robots = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, "/robots.txt"));
            foreach (var agent in request.Headers.UserAgent)
                robots.Headers.UserAgent.Add(agent);
            return base.SendAsync(robots, ct);
        }, logger, cancellationToken);

        if (rules is not null && rules.Allows(uri.PathAndQuery))
            return await base.SendAsync(request, cancellationToken);

        if (rules is not null)
            logger.LogWarning("robots.txt of {Host} disallows {Path}; not requested.", uri.Host, uri.AbsolutePath);
        return new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = RefusedReason, RequestMessage = request };
    }
}
