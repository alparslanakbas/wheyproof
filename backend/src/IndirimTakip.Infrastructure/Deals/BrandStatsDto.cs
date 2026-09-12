namespace IndirimTakip.Infrastructure.Deals;

public record BrandStatsDto(
    int TotalProducts,
    int DiscountCount,
    int ThirtyDayLowCount,
    // Average discount percentage of discounted products; null when there are no
    // discounts (the field stays empty rather than showing a made-up "0%").
    double? AverageDiscountPercent,
    DateTimeOffset? LastScanAt,
    // Average current price of the products in scope. On brand x category pages
    // it is compared with the category as a whole to say where the brand stands;
    // that is the only original content on those pages. Null without products.
    decimal? AveragePrice = null);
