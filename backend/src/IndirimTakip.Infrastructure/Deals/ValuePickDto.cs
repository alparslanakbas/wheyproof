namespace IndirimTakip.Infrastructure.Deals;

public sealed record ValuePickDto(
    int ProductId,
    string ProductName,
    string BrandName,
    string? ImageUrl,
    string? Size,
    // The store selling the product; null means the brand's own store.
    string? Seller,
    decimal CurrentPrice,
    decimal PricePerKg,
    // The highest price of the last 30 days; the discount is measured against it.
    decimal ReferencePrice,
    // Discount from our own price history (same calculation as DealDto); 0 if none.
    decimal DiscountPercent,
    bool IsAtThirtyDayLow,
    // Out-of-stock products never make the list; null = the store doesn't report stock.
    bool? InStock,
    // Store URL with the affiliate code applied (see DealDto.StoreUrl).
    string StoreUrl);

/// <param name="EligibleCount">Products with a computable price per kg that passed the guards (before one per brand is taken).</param>
public sealed record ValuePicksDto(IReadOnlyList<ValuePickDto> Items, int EligibleCount);
