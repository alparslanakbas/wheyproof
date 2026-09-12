namespace IndirimTakip.Infrastructure.Deals;

// For the brands directory: the number of tracked products per brand.
//
// A SEPARATE ENDPOINT WAS NEEDED because the directory page first computed this
// number by summing BrandCategoryPairDto rows, and that list only contains
// products THAT HAVE A CATEGORY. The result: one brand showed two different
// numbers (the directory said 85 products, the brand page 113, and the
// schema.org FAQ block 113 too). Across the catalog 16% of products weren't
// counted, and brands with no categorised products showed "0 products".
//
// This count uses the SAME definition as GetBrandStatsAsync (active brand + not
// stale), so both pages show the same number.
public record BrandProductCountDto(string BrandName, int ProductCount);
