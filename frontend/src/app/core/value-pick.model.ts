// /api/value-picks — the product list of the "Which supplement?" pages, ranked
// by price per kg within a category, one product per brand (see the backend's
// ValuePickRanker).
export interface ValuePick {
  productId: number;
  productName: string;
  brandName: string;
  imageUrl: string | null;
  size: string | null;
  // The store selling the product; null means the brand's own store.
  seller: string | null;
  currentPrice: number;
  pricePerKg: number;
  referencePrice: number;
  // Discount from our own price history; 0 if none.
  discountPercent: number;
  isAtThirtyDayLow: boolean;
  inStock: boolean | null;
  storeUrl: string;
}

export interface ValuePicks {
  items: ValuePick[];
  // Products that passed the guards and have a computable price per kg.
  eligibleCount: number;
}
