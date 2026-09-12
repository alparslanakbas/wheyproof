using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Images;

/// <summary>
/// Downloads product images that don't have a local copy yet.
/// </summary>
/// <remarks>
/// <b>WHY IT KEEPS NO STATE.</b> "Download what has no local copy" is idempotent:
/// a missed run is made up by the next one, and finished work isn't repeated. So
/// the "a timer holding the period gets reset by every deploy" trap, hit twice
/// with the digest and the detail backfill, can't happen here and no separate
/// stamp record is needed.
///
/// <b>WHY A QUOTA.</b> The first run faces thousands of images. Pulling them all
/// at once would load every source at the same time; a per-run limit is polite to
/// the other side and makes being cut off by a deploy cheap.
///
/// <b>CLEANUP IS SEPARATE AND RARE.</b> Deleting leftover files requires reading
/// the whole catalog; doing it every run is pointless. It runs only when nothing
/// is left to download, i.e. when the work is done.
/// </remarks>
public sealed class ProductImageBackgroundService(
    IServiceScopeFactory scopeFactory,
    ProductImageStore store,
    ProductImageOptions options,
    ILogger<ProductImageBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Product image download is disabled.");
            return;
        }

        // Waiting a little at startup is deliberate: right after the container
        // starts, migrations and the first scrape run, and image downloads
        // shouldn't get ahead of them.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.IntervalMinutes));

        do
        {
            try
            {
                var downloaded = await RunOnceAsync(stoppingToken);
                if (downloaded == 0)
                    await CleanUpLeftoversAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Product image run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Returns the number of images downloaded in this run.</summary>
    private async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Hidden products are included (IgnoreQueryFilters): when visibility is
        // turned back on the image should be ready; starting the download then
        // would leave the card empty for a run.
        var candidates = await db.Products
            .IgnoreQueryFilters()
            .Where(p => p.ImageUrl != null && p.ImageUrl != "" && p.LocalImagePath == null)
            .OrderByDescending(p => p.ClickCount)
            .ThenByDescending(p => p.Id)
            .Select(p => new { p.Id, p.ImageUrl })
            .Take(options.MaxPerRun)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return 0;

        var succeeded = 0;
        foreach (var candidate in candidates)
        {
            var fileName = await store.DownloadAsync(candidate.ImageUrl!, cancellationToken);
            if (fileName is null)
                continue;

            // Written one by one: if the run is cut off, the work done so far is
            // saved. A batch save would leave files downloaded in a cut-off run
            // looking as if they had never been downloaded.
            await db.Products
                .IgnoreQueryFilters()
                .Where(p => p.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalImagePath, fileName), cancellationToken);

            succeeded++;
        }

        logger.LogInformation(
            "Product images: {Attempted} attempted, {Succeeded} downloaded.", candidates.Count, succeeded);

        return succeeded;
    }

    private async Task CleanUpLeftoversAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inUse = await db.Products
            .IgnoreQueryFilters()
            .Where(p => p.LocalImagePath != null)
            .Select(p => p.LocalImagePath!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var deleted = store.DeleteUnused(inUse.ToHashSet(StringComparer.Ordinal));
        if (deleted > 0)
            logger.LogInformation("Product image cleanup: {Deleted} leftover files deleted.", deleted);
    }
}
