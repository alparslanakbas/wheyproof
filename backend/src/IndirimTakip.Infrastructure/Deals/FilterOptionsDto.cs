namespace IndirimTakip.Infrastructure.Deals;

// Sellers: where the product is bought. Not the same as the brand
// (manufacturer): in a retailer catalog a brand's product may be sold by the
// retailer. The first item is the label that selects products sold on the
// brand's own store (DealsQueryService.BrandDirectSellerLabel).
public record FilterOptionsDto(
    IReadOnlyList<string> Brands,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Sellers);
