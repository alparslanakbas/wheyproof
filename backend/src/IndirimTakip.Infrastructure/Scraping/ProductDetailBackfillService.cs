using IndirimTakip.Core.Entities;
using IndirimTakip.Core.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

// For stores whose description and nutrition don't come with the regular scrape
// (they implement IProductDetailFetcher), fills both with ONE HTTP request per
// product. Stores whose regular scrape already carries both (e.g. Shopify's
// body_html) are never touched.
//
// A description, once filled, is permanent. Nutrition uses the NutritionCheckedAt
// stamp instead: many products (accessories, bars, snacks) really have no table,
// and without the stamp the same products would be retried forever.
public class ProductDetailBackfillService(
    AppDbContext db,
    IEnumerable<IBrandScraper> scrapers,
    ILogger<ProductDetailBackfillService> logger)
{
    // Courtesy delay between product requests so the store isn't hammered.
    private static readonly TimeSpan DelayBetweenProducts = TimeSpan.FromMilliseconds(750);

    // At most this many products are tried in one run. Progressing gradually
    // instead of pulling every gap at once keeps a run reasonably short and
    // limits the impact if something goes wrong.
    //
    // Raised from 60 to 150 after measuring: with the catalog grown several
    // times over, 60 products a week would have needed about 11 weeks to close
    // the accumulated gap. The load stays small: 150 products split across the
    // fetchers is a few dozen requests each, 750 ms apart, roughly half a minute
    // of traffic per source.
    private const int MaxProductsPerRun = 150;

    // WHETHER IT'S TIME TO RUN IS DECIDED BY THE RUN'S OWN COMPLETION RECORD,
    // NOT BY PRODUCT STAMPS.
    //
    // MAX(Products.NutritionCheckedAt) was used before, and the reasoning was
    // sound: only this service writes that stamp. But it missed one case: a run
    // cut off halfway. It happened in production: a run started, processed ONE
    // product, was cancelled when a deploy recreated the container, and that
    // single stamp pushed the schedule back a full interval. On a day with many
    // deploys the job could keep being postponed without progressing.
    //
    // Scheduling still lives in the DATABASE (not memory): the period is measured
    // in days, and in memory every deploy would reset the counter.
    public async Task<bool> IsDueAsync(int intervalDays, CancellationToken cancellationToken = default)
    {
        var lastCompleted = await db.BackgroundJobRuns
            .Where(j => j.JobName == BackgroundJobNames.DetailBackfill)
            .Select(j => (DateTimeOffset?)j.LastCompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return lastCompleted is null || lastCompleted < DateTimeOffset.UtcNow.AddDays(-intervalDays);
    }

    public async Task<int> BackfillAsync(CancellationToken cancellationToken = default)
    {
        var totalUpdated = 0;
        var totalAttempted = 0;

        // THE QUOTA IS SPLIT EQUALLY ACROSS STORES.
        //
        // The loop used to consume the quota IN ORDER: as long as the first
        // store's gap wasn't closed, the others never got a turn. Measured on the
        // Turkish site: one store had 84% of its products untouched for weeks.
        // With a small quota per run, the next store in line would wait months.
        //
        // Each store gets the ceiling of quota / stores. A store that doesn't use
        // its share (its gap is closed) releases it, because `remaining` is
        // recomputed from the attempts actually made.
        var fetchers = scrapers.OfType<IProductDetailFetcher>().Where(f => f.HasProductDetails).ToList();

        // Checked products come back after 30 days: a store can add or change
        // a panel. Without a limit a US page, which never yields a description,
        // matched "Description == null" forever and was downloaded every run.
        var recheckBefore = DateTimeOffset.UtcNow.AddDays(-30);
        if (fetchers.Count == 0)
            return 0;

        var perBrandQuota = (int)Math.Ceiling((double)MaxProductsPerRun / fetchers.Count);

        foreach (var scraper in fetchers)
        {
            var brandScraper = (IBrandScraper)scraper;
            var remaining = Math.Min(perBrandQuota, MaxProductsPerRun - totalAttempted);
            if (remaining <= 0)
                break;

            // Products whose description OR nutrition hasn't been checked yet.
            // (Some already have a description from an earlier backfill but a null
            // NutritionCheckedAt; they are targets too.)
            // Seller == null IS REQUIRED: it means the product comes from the
            // BRAND'S OWN STORE, and the scraper only knows that site's markup.
            //
            // Without it, selection looked only at the brand name and retailer
            // copies became targets too: measured, 37 of 38 products checked for
            // one brand were a retailer's URLs, fetched with the brand's parser and
            // all empty. It did two kinds of harm: pointless requests to a third
            // party's site, and those rows got a "checked" stamp and were NEVER
            // tried again.
            // ORDERING IS REQUIRED, otherwise the list doesn't advance.
            //
            // "Description == null" is NEVER false for some stores: their
            // description isn't in the server HTML and the fetcher deliberately
            // returns null. An unordered query brought back the same first rows
            // every run; measured, 25 requests went out and the checked count
            // never moved, because the same 25 pages were downloaded again and
            // again.
            //
            // Never-checked products first, then the ones checked longest ago:
            // every run advances, and old records get refreshed over time (a
            // store may have added a nutrition table later).
            var missingProducts = await db.Products
                .Where(p => p.Brand!.Name == brandScraper.BrandName
                    && p.Seller == null
                    && (p.NutritionCheckedAt == null || p.NutritionCheckedAt < recheckBefore))
                .OrderBy(p => p.NutritionCheckedAt == null ? 0 : 1)
                .ThenBy(p => p.NutritionCheckedAt)
                .ThenBy(p => p.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            if (missingProducts.Count == 0)
                continue;

            logger.LogInformation(
                "{Brand}: {Count} products are missing details; filling them in.", brandScraper.BrandName, missingProducts.Count);

            foreach (var product in missingProducts)
            {
                totalAttempted++;
                try
                {
                    var details = await scraper.FetchDetailsAsync(product.Url, cancellationToken);

                    // ??= on purpose: it doesn't overwrite an existing (more reliable) value.
                    product.Description ??= details.Description;
                    product.NutritionJson ??= details.NutritionJson;
                    product.ProteinPerServingGrams ??= details.ProteinPerServingGrams;

                    // Serving information the source DECLARES directly comes first;
                    // inference from text only applies when it's missing (a derived
                    // value must not overwrite the declaration).
                    product.ServingSizeGrams ??= details.ServingSizeGrams;
                    product.ServingsPerPackage ??= details.ServingsPerPackage;

                    // The description may mention the serving size too ("1 scoop
                    // (30 g)"); used only when the scraper gave no structured value.
                    if (details.Description is not null)
                        product.ServingSizeGrams ??= ProductAttributeParser.ExtractServingSizeGrams(details.Description);

                    // The page was checked successfully: stamped whether or not a
                    // table was found, so it isn't retried forever.
                    product.NutritionCheckedAt = DateTimeOffset.UtcNow;
                    product.PageNotFoundAt = null;
                    totalUpdated++;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Not stamped, so it comes back next run for the second check.
                    if (RecordPageNotFound(product, DateTimeOffset.UtcNow))
                    {
                        logger.LogWarning(
                            "{Brand} - product page still 404 since {Since}; hidden: {Url}.",
                            brandScraper.BrandName, product.PageNotFoundAt, product.Url);
                    }
                    else
                    {
                        logger.LogWarning("{Brand} - product page 404, will check again: {Url}.", brandScraper.BrandName, product.Url);
                    }
                }
                catch (Exception ex)
                {
                    // One product's error (404, transient network issue) must not
                    // stop the rest. No stamp either, so it is retried next run.
                    logger.LogWarning(ex, "{Brand} - could not fetch details for {Url}.", brandScraper.BrandName, product.Url);
                }

                await Task.Delay(DelayBetweenProducts, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        // THE COMPLETION STAMP IS SET HERE, AT THE END OF THE LOOP.
        //
        // This line is reached only when the run finished from start to end: a
        // cancelled run (deploy, container recreation) throws at the `Task.Delay`
        // between products and leaves the method before getting here. So a run
        // cut off halfway does NOT move the schedule; the next check says it's
        // due again.
        await MarkCompletedAsync(cancellationToken);

        return totalUpdated;
    }

    // A second 404 has to come at least this long after the first. Runs are a
    // day apart in production, so in practice this is "404 on two different
    // days"; the gap keeps a manual re-run minutes later from hiding anything.
    internal static readonly TimeSpan PageNotFoundConfirmAfter = TimeSpan.FromHours(12);

    /// <summary>
    /// Records a 404 on the product page; returns true when this hides the product.
    /// </summary>
    /// <remarks>
    /// First 404: remember when. A 404 again after <see cref="PageNotFoundConfirmAfter"/>:
    /// hide it. Measured 2026-09-16: Naked Nutrition kept 15 rows in products.json
    /// whose US pages 404 (EU/UK editions), and 4 of them carry no tag the scraper
    /// could filter on. A hidden product is left alone afterwards (the global
    /// filter keeps it out of this query too); the admin panel can show it again.
    /// </remarks>
    internal static bool RecordPageNotFound(Product product, DateTimeOffset now)
    {
        if (product.PageNotFoundAt is null)
        {
            product.PageNotFoundAt = now;
            return false;
        }

        if (now - product.PageNotFoundAt.Value < PageNotFoundConfirmAfter)
            return false;

        product.IsActive = false;
        return true;
    }

    private async Task MarkCompletedAsync(CancellationToken cancellationToken)
    {
        var run = await db.BackgroundJobRuns
            .FirstOrDefaultAsync(j => j.JobName == BackgroundJobNames.DetailBackfill, cancellationToken);

        if (run is null)
        {
            db.BackgroundJobRuns.Add(new BackgroundJobRun
            {
                JobName = BackgroundJobNames.DetailBackfill,
                LastCompletedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            run.LastCompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
