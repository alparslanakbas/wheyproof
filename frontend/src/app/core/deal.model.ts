export interface Deal {
  productId: number;
  productName: string;
  productUrl: string;
  imageUrl: string | null;
  category: string | null;
  size: string | null;
  flavor: string | null;
  // Could it be bought at the store at the last check?
  //
  // null = "this source doesn't report stock"; must NOT be confused with
  // false. Counting the unknown as "in stock" would be invented data.
  inStock: boolean | null;
  // The store that SELLS the product; null means the brand's own site. For
  // retailer sources the brand (maker) and the seller differ: a product shows
  // under "Elev8" but is sold by bodybuilding.com.
  seller: string | null;
  // The store address with the affiliate code added. "Go to store" links here
  // directly, with no /go/{id} redirect in between: in an installed PWA that
  // redirect broke the back button.
  storeUrl: string | null;
  // When this page is a copy of ANOTHER product page, the main page's id.
  // Stores publish the same product at several addresses; the pages are
  // identical, so Google counted them as duplicates. When set, the canonical
  // points to the main page. NULL = this is the main page.
  canonicalProductId: number | null;
  servingSizeGrams: number | null;
  // Servings per package as the brand states it directly.
  servingsPerPackage: number | null;
  // The store's own product description, when it provides one; otherwise null.
  description: string | null;
  // The nutrition table as normalized JSON ({"Protein": "24 g", ...}), only
  // when the brand provides it reliably.
  nutritionJson: string | null;
  proteinPerServingGrams: number | null;
  brandName: string;
  currentPrice: number;
  referencePrice: number;
  discountPercent: number;
  storeOldPrice: number | null;
  storeDiscountPercent: number | null;
  scrapedAt: string;
  isAtThirtyDayLow: boolean;
  // The customer rating on the store's OWN site; not our review, and labeled
  // that way in the UI. Only set for stores that collect reviews. Not
  // comparable across stores (each uses its own review system), so ranking
  // uses the review count, not the rating.
  ratingValue: number | null;
  ratingCount: number | null;
  // Only from the single-product endpoint; lists already hide frozen
  // records, so there these keep their defaults.
  isStale?: boolean;
  replacementProductId?: number | null;
}
