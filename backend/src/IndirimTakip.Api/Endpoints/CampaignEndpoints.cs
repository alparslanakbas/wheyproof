using IndirimTakip.Infrastructure.Deals;

namespace IndirimTakip.Api.Endpoints;

// Season page (/black-friday): the products stores show "on sale", classified against the lowest price in the 30
// days before the discount started (see CampaignDiscountService). In the public data cache, which is cleared after
// every scrape.
internal static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this WebApplication app, string cachePolicy)
    {
        app.MapGet("/api/campaign/summary", async (CampaignDiscountService campaign, CancellationToken ct) =>
            Results.Ok(await campaign.SummaryAsync(ct)))
            .CacheOutput(cachePolicy);
    }
}
