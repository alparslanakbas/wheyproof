using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Scraping;

// IndexNow: the shared notification protocol of Bing, Yandex, Seznam and others.
// When a page changes, we tell the search engines directly instead of waiting for
// them to crawl us again.
//
// Why it was added: on the Turkish site, several AI assistants couldn't find the
// site at all. The cause turned out to be Bing: Google had hundreds of pages
// indexed while Bing had practically none. There was no technical block (Bingbot
// fetched the home page and sitemap fine, robots.txt blocked nobody); the sitemap
// had been submitted but not processed, a known behavior for new sites without
// external links. Most AI tools use Bing's index, so this is a direct visibility
// problem.
//
// The key is NOT a secret: it is published at `https://{host}/{key}.txt` to prove
// site ownership.
public class IndexNowClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<IndexNowClient> logger)
{
    // The protocol accepts at most 10,000 URLs per request.
    private const int MaxUrlsPerRequest = 10_000;

    public bool IsEnabled =>
        configuration.GetValue("IndexNow:Enabled", true)
        && !string.IsNullOrWhiteSpace(configuration["IndexNow:Key"]);

    /// <summary>Notifies search engines of the given URLs. Returns the number of URLs sent.</summary>
    public async Task<int> SubmitAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken = default)
    {
        if (urls.Count == 0) return 0;

        var key = configuration["IndexNow:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogInformation("No IndexNow key configured; notification skipped.");
            return 0;
        }

        if (!configuration.GetValue("IndexNow:Enabled", true))
            return 0;

        var frontendBaseUrl = (configuration["FrontendBaseUrl"] ?? "https://www.wheyproof.com").TrimEnd('/');
        var host = new Uri(frontendBaseUrl).Host;

        var sent = 0;
        foreach (var batch in urls.Chunk(MaxUrlsPerRequest))
        {
            var payload = new
            {
                host,
                key,
                keyLocation = $"{frontendBaseUrl}/{key}.txt",
                urlList = batch,
            };

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.indexnow.org/indexnow", payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    sent += batch.Length;
                    logger.LogInformation("IndexNow: {Count} URLs submitted ({Status}).", batch.Length, (int)response.StatusCode);
                }
                else
                {
                    // 403 = key couldn't be verified, 422 = URL doesn't match the host,
                    // 429 = too many requests. None of them should stop scraping.
                    logger.LogWarning(
                        "IndexNow submission rejected: {Status}. URLs sent: {Count}.",
                        (int)response.StatusCode, batch.Length);
                }
            }
            catch (Exception ex)
            {
                // Notification is secondary work; its failure must never break
                // scraping or the calling flow.
                logger.LogWarning(ex, "Could not send the IndexNow submission.");
            }
        }

        return sent;
    }
}
