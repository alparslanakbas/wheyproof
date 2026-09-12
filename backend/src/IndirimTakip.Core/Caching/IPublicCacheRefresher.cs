namespace IndirimTakip.Core.Caching;

/// <summary>
/// Refreshes the public data cache when a scrape finishes.
///
/// <b>WHY THERE IS AN INTERFACE HERE.</b> The actual work is done with ASP.NET's
/// output cache (<c>IOutputCacheStore</c>), but the trigger is the scraping
/// services in Infrastructure. Depending on the concrete type would mean
/// referencing the whole web framework (<c>Microsoft.AspNetCore.App</c>) from
/// Infrastructure, and scrapers have no reason to know about the HTTP server. The
/// interface lives here, the implementation in the Api project.
///
/// Without a registered implementation <see cref="NullPublicCacheRefresher"/>
/// steps in; a scrape never fails because of the cache.
/// </summary>
public interface IPublicCacheRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op implementation used where there is no cache layer (tests, console tools).</summary>
public sealed class NullPublicCacheRefresher : IPublicCacheRefresher
{
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
