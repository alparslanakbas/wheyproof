namespace IndirimTakip.Infrastructure.Deals;

// For brand x category pages: how many products each brand has in each
// category. Only pairs that HAVE products are returned; opening pages for empty
// combinations would produce thin content.
public record BrandCategoryPairDto(string BrandName, string Category, int ProductCount);
