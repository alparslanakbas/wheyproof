export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface FilterOptions {
  brands: string[];
  categories: string[];
  // Where the product is bought, not the same thing as the brand (maker).
  // Empty when there are no retailer products, and the UI then hides the
  // seller select. The first entry is the "brand's own store" label (Seller =
  // NULL in the backend).
  sellers: string[];
}
