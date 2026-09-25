using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Api.Endpoints;

// Price history chart and the mini sparklines under product cards.
internal static class PriceHistoryEndpoints
{
    public static void MapPriceHistoryEndpoints(this WebApplication app, string cachePolicy)
    {
        app.MapGet("/api/products/{id:int}/price-history", async (int id, int? days, PriceHistoryQueryService service, CancellationToken ct) =>
        {
            var windowDays = EndpointHelpers.NormalizeHistoryDays(days);
            var result = await service.GetPriceHistoryAsync(id, windowDays, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).CacheOutput(cachePolicy);

        // Batch endpoint for the mini sparklines on product cards: one request
        // per page (24 cards) instead of N+1. The id count should stay near the
        // page size; it is capped at 100 against abuse (same pattern as
        // NormalizePageSize).
        app.MapGet("/api/products/sparklines", async (int[] ids, int? days, PriceHistoryQueryService service, CancellationToken ct) =>
        {
            var windowDays = EndpointHelpers.NormalizeHistoryDays(days);
            var limitedIds = ids.Distinct().Take(100).ToList();
            var result = await service.GetSparklinesAsync(limitedIds, windowDays, ct);
            return Results.Ok(result);
        }).CacheOutput(cachePolicy);
    }
}
