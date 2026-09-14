using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Reads queued nutrition label images in small batches.
/// </summary>
/// <remarks>
/// <b>Keeps no state</b>, like the product image job: "read what hasn't been
/// read at its current URL" is idempotent, so a deploy cutting a run short
/// costs nothing and no schedule stamp is needed.
///
/// <b>Off until the pilot is reviewed.</b> Without Enabled (and a key) it logs
/// once and stops; /api/dev/nutrition-labels/pilot reads without writing.
/// </remarks>
public sealed class NutritionLabelBackgroundService(
    IServiceScopeFactory scopeFactory,
    NutritionLabelOptions options,
    ILogger<NutritionLabelBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool available;
        using (var scope = scopeFactory.CreateScope())
            available = scope.ServiceProvider.GetRequiredService<INutritionLabelReader>().IsAvailable;

        if (!options.Enabled || !available)
        {
            logger.LogInformation("Nutrition label reading is off (enabled: {Enabled}, engine: {Engine}, available: {Available}).",
                options.Enabled, options.Engine, available);
            return;
        }

        try
        {
            // After migrations and the startup scrape, which fills the queue.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nutrition label run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<NutritionLabelService>();

        var queue = await service.QueueAsync(options.MaxPerRun, source: null, cancellationToken);
        if (queue.Count == 0)
            return;

        int accepted = 0, rejected = 0, retryLater = 0;
        foreach (var item in queue)
        {
            var outcome = await service.ProcessAsync(item, write: true, model: null, cancellationToken);
            if (outcome is null) retryLater++;
            else if (outcome.Accepted) accepted++;
            else rejected++;
        }

        logger.LogInformation(
            "Nutrition labels: {Accepted} accepted, {Rejected} rejected, {Retry} to retry ({Queued} queued this run).",
            accepted, rejected, retryLater, queue.Count);
    }
}
