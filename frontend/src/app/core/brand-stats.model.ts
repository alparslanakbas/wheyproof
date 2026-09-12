export interface BrandStats {
  totalProducts: number;
  discountCount: number;
  thirtyDayLowCount: number;
  averageDiscountPercent: number | null;
  lastScanAt: string | null;
  // Average current price of the products in scope, compared with the whole
  // category on brand x category pages.
  averagePrice: number | null;
}
