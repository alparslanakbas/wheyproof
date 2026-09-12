namespace IndirimTakip.Infrastructure.Deals;

// For the product review page's "how this product compares in its category"
// section: the average current price of the active products in that category.
// Instead of turning one product's price into a made-up "score", it offers an
// objective comparison with the real category average.
public record CategoryPriceStatsDto(int ProductCount, decimal AveragePrice, decimal MinPrice, decimal MaxPrice);
