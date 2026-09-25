using IndirimTakip.Core.Caching;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.NutritionLabels;
using IndirimTakip.Infrastructure.Catalog;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Api.Endpoints;

// Admin endpoints (/api/dev/*). ALL of them require X-Admin-Key (or the
// admin session); unprotected, anyone could trigger scrapes and add fake
// coupons.
internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app, string? adminApiKey)
    {
        app.MapAdminCatalogEndpoints(adminApiKey);
        app.MapAdminSubscriberEndpoints(adminApiKey);
        app.MapAdminMonitoringEndpoints(adminApiKey);
    }
}

internal record VisibilityRequest(bool IsActive);

/// <summary>A category slug, or null to hand the product back to the automatic category.</summary>
internal record ProductCategoryRequest(string? Category);
