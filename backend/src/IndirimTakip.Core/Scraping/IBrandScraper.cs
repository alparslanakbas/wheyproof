namespace IndirimTakip.Core.Scraping;

public interface IBrandScraper
{
    string BrandName { get; }
    string BaseUrl { get; }

    Task<IReadOnlyList<ScrapedProduct>> ScrapeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Should this source be LEFT OUT of the regular scrape round (every 6 hours)
    /// and run separately once a day?
    ///
    /// Defaults to false: most sources answer from a single JSON/HTML endpoint, so
    /// frequent scraping costs nothing. Some retailer sites render the product
    /// list in the browser and need one request per product; pulling 900+
    /// products every 6 hours is slow, heavy on the other server, and raises the
    /// risk of being blocked.
    /// </summary>
    bool DailyOnly => false;
}
