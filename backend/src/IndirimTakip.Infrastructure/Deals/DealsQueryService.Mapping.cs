using IndirimTakip.Infrastructure.Images;

namespace IndirimTakip.Infrastructure.Deals;

// The DealDto mapping shared by every list, favorites and the product page. A separate file because
// DealsQueryService.cs is at its size cap (scripts/size-limit.sh); moved out on 2026-10-20 with the "lowest since"
// badge, the same split the Turkish site made for the same badge.
public partial class DealsQueryService
{
    private DealDto MapToDealDto(DealRow row)
    {
        var latest = row.Latest;
        // Single queries read the current price live, the reference from the summary: no negative discount mid-round.
        var referencePrice = Math.Max(row.ReferencePrice, latest.Price);
        var atThirtyDayLow = latest.Price <= row.ThirtyDayLowPrice && row.ThirtyDayLowPrice < referencePrice;
        return new DealDto(
            row.Product.Id, row.Product.Name, row.Product.Url,
            // The local copy if there is one, otherwise the source URL. An
            // in-memory mapping, not a query; the SQL-producing part of this
            // file isn't touched.
            ProductImageStore.PublicUrl(row.Product.LocalImagePath, imageBaseUrl) ?? row.Product.ImageUrl,
            row.Product.Category, row.Product.Size, row.Product.Flavor, row.Product.ServingSizeGrams,
            row.Product.ServingsPerPackage,
            row.Product.Description,
            row.Product.NutritionJson,
            row.Product.ProteinPerServingGrams,
            row.BrandName, latest.Price, referencePrice,
            // The reference price can be zero (a product listed without a
            // price); the store discount line right below already guarded
            // against it, but this one didn't, and when sorted by price that
            // product came first and the whole list failed with division by zero.
            referencePrice > 0 ? Math.Round((referencePrice - latest.Price) / referencePrice * 100, 1) : 0m,
            latest.StoreOldPrice,
            latest.StoreOldPrice is decimal storeOld && storeOld > 0
                ? Math.Round((storeOld - latest.Price) / storeOld * 100, 1)
                : null,
            latest.ScrapedAt,
            atThirtyDayLow,
            IsStale: false,
            ReplacementProductId: null,
            RatingValue: row.Product.RatingValue,
            RatingCount: row.Product.RatingCount,
            InStock: row.Product.InStock,
            Seller: row.Product.Seller,
            AffiliateLinkBuilder.Apply(row.Product.Url, affiliateOptions.Value),
            LowestSince: TrackedLow.Since(atThirtyDayLow, latest.Price,
                row.Product.TrackedSince, row.Product.LowestTrackedPrice, DateTimeOffset.UtcNow));
    }
}
