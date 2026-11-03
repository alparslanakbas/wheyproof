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

    /// <summary>
    /// The longest Crawl-delay we wait out between requests. A host asking for more is not read at all
    /// (it would hold a cycle for hours), which still never reads it faster than asked.
    /// </summary>
    internal static readonly TimeSpan LongestCrawlDelay = TimeSpan.FromMinutes(5);

    private sealed record Entry(RobotsRules? Rules, DateTimeOffset NextFetch);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> paceGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Waits until <paramref name="crawlDelay"/> has passed since the last request to
    /// <paramref name="host"/> (from any client), then counts this one. False, without waiting, when
    /// the delay is longer than <see cref="LongestCrawlDelay"/>.
    /// </summary>
    public async Task<bool> PaceAsync(string host, TimeSpan crawlDelay, CancellationToken cancellationToken)
    {
        if (crawlDelay > LongestCrawlDelay)
            return false;

        var gate = paceGates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (lastRequests.TryGetValue(host, out var last))
            {
                var wait = last + crawlDelay - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, time, cancellationToken);
            }
            lastRequests[host] = time.GetUtcNow();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

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
/// <see cref="RobotsRules"/>), and not sooner than its Crawl-delay after the last one. A refused
/// request is not sent: it gets a local 403 whose reason says why, so the scraper treats it as a
/// failed source and the store goes stale rather than being read.
/// </summary>
/// <remarks>
/// Outermost on the Shopify client, so robots.txt itself is fetched through the rest of the chain,
/// with the bot's User-Agent and signed if signing is on.
/// </remarks>
public sealed class RobotsTxtHandler(RobotsTxtCache cache, ILogger<RobotsTxtHandler> logger, IReadOnlySet<string>? onlyHosts = null)
    : DelegatingHandler
{
    /// <summary>The name robots.txt files address us by (our User-Agent product token).</summary>
    public const string ProductToken = "wheyproofbot";

    internal const string RefusedReason = "Disallowed by robots.txt";
    internal const string CrawlDelayReason = "Crawl-delay of robots.txt is longer than we wait";

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
        {
            if (rules.CrawlDelay is { } delay && !await cache.PaceAsync(uri.Host, delay, cancellationToken))
            {
                logger.LogWarning("robots.txt of {Host} sets a Crawl-delay of {Delay}, longer than we wait; not requested.",
                    uri.Host, delay);
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = CrawlDelayReason, RequestMessage = request };
            }
            return await base.SendAsync(request, cancellationToken);
        }

        if (rules is not null)
            logger.LogWarning("robots.txt of {Host} disallows {Path}; not requested.", uri.Host, uri.AbsolutePath);
        return new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = RefusedReason, RequestMessage = request };
    }
}
