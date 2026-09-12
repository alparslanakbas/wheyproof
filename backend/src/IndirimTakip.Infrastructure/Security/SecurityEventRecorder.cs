using IndirimTakip.Core.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Security;

/// <summary>
/// Writes security events to the database.
/// </summary>
/// <remarks>
/// <b>IT OPENS ITS OWN SCOPE, because of a proven data loss.</b> It used to use
/// the request's own <c>AppDbContext</c>. One kind of recorded event, 5xx, happens
/// precisely because a <c>SaveChangesAsync</c> blew up, and at that moment the
/// context is DIRTY: the failed entities are still tracked. Recording through the
/// same context meant retrying the failed write; it hit the same error and the
/// event was lost silently.
///
/// <b>Measured, not assumed:</b> a column limit was exceeded on purpose, the
/// request returned 500 and the event NEVER reached the table; the container log
/// held a "could not record security event ... value too long" warning. So the
/// most interesting class of server errors (requests that fail while writing to
/// the database) never got recorded, and nothing showed that as an error.
/// </remarks>
public class SecurityEventRecorder(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    ILogger<SecurityEventRecorder> logger)
{
    /// <summary>Maximum events one address can write in a window.</summary>
    /// <remarks>
    /// THE LOG ITSELF MUST NOT BECOME AN ATTACK SURFACE. Without a limit, a scanner
    /// producing hundreds of 404s a second would produce hundreds of INSERTs a
    /// second; the log would turn into the attacker's tool for bloating the
    /// database. The first 30 events already prove the pattern; the rest repeats
    /// the same information.
    /// </remarks>
    private const int MaxPerWindow = 30;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        if (!HasQuota(securityEvent.Ip))
            return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.SecurityEvents.Add(securityEvent);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Failing to log must NOT BREAK THE REQUEST. A request landing here is
            // already faulty or unauthorized; adding a 500 on top would signal
            // "something breaks here" to an attacker.
            logger.LogWarning(ex, "Could not record security event: {Kind} {Path}", securityEvent.Kind, securityEvent.Path);
        }
    }

    /// <summary>True if the address hasn't used up its quota in this window.</summary>
    private bool HasQuota(string ip)
    {
        // In-memory counter that cleans itself up with a TTL. A permanent
        // dictionary would itself become a memory problem under an attack from
        // many different addresses.
        var counter = cache.GetOrCreate("security-event:" + ip, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new Counter();
        })!;

        return Interlocked.Increment(ref counter.Count) <= MaxPerWindow;
    }

    private sealed class Counter
    {
        public int Count;
    }
}
