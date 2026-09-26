using Microsoft.AspNetCore.OutputCaching;

namespace IndirimTakip.Api.Caching;

/// <summary>
/// The rules of the public data cache. Program.cs and the tests use the SAME
/// setup; a copy in the test would test the copy, not the real thing, once an
/// order or a rule changed.
/// </summary>
public static class PublicDataCache
{
    public static void Apply(OutputCachePolicyBuilder policy, TimeSpan duration) => policy
        .Expire(duration)
        // The post-scrape purge works by this tag.
        .Tag(OutputCacheRefresher.Tag)
        // Only the query parameters the endpoint binds are in the key (26 Sept).
        // It used to be "*": right so that filters never saw each other's
        // results, but a random parameter skipped the cache. The reasoning is
        // on the policy itself.
        .AddPolicy<EndpointQueryKeysPolicy>()
        // HOST IS NOT PART OF THE KEY (16 Sept). SSR now reaches the API over
        // the Docker network (http://wheyproof-backend:8080). Node's fetch won't
        // send a custom Host header (measured), so internal requests arrive
        // with the container name as Host; with Host in the key they would never
        // see the warmed entries (Host: api.wheyproof.com) and every page would
        // hit the database cold. These responses don't depend on Host and the
        // API is served from one public address. The scheme STAYS in the key:
        // SSR and the warmup both send X-Forwarded-Proto: https.
        .SetVaryByHost(false);
}
