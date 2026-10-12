namespace IndirimTakip.Infrastructure.Images;

/// <summary>
/// Keeps a product whose image could not be downloaded out of the candidate
/// list for a while.
/// </summary>
/// <remarks>
/// A failed image leaves the local path empty, so the product became a
/// candidate again every run, i.e. every 10 minutes: 144 requests a day to a
/// dead address (live on Oct 12: one URL returning 404, one AVIF that
/// ImageSharp cannot read). The backoff lives only in memory; if a deploy
/// resets it, the address is tried once more, and that is the whole cost. It
/// is keyed by product id: if the source changes the image URL, the new URL
/// is tried when the backoff ends.
/// </remarks>
internal sealed class FailedImageBackoff
{
    private readonly Dictionary<int, DateTimeOffset> failures = [];

    public void Failed(int productId, DateTimeOffset at) => failures[productId] = at;

    /// <summary>Drops expired entries and returns the products still backing off.</summary>
    public IReadOnlySet<int> Waiting(DateTimeOffset at, TimeSpan duration)
    {
        foreach (var id in failures.Where(f => at - f.Value >= duration).Select(f => f.Key).ToList())
            failures.Remove(id);
        return failures.Keys.ToHashSet();
    }
}
