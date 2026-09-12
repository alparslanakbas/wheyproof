import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, of, shareReplay, throwError } from 'rxjs';

import { API_BASE_URL } from './api.config';
import { BrandStats } from './brand-stats.model';
import { CategoryPriceStats } from './category-price-stats.model';
import { Deal } from './deal.model';
import { HomepageStats } from './homepage-stats.model';
import { FilterOptions, PagedResult } from './paged-result.model';
import { ProductSparkline } from './product-sparkline.model';

// Brand x category intersection: how many products a brand has per category.
export interface BrandCategoryPair {
  brandName: string;
  category: string;
  productCount: number;
}

// Product count in the brand directory. SUMMING the brand x category pairs
// gives the wrong total (that list only counts categorised products), so this
// has its own endpoint with the same definition as the brand page.
export interface BrandProductCount {
  brandName: string;
  productCount: number;
}

export interface DealsQuery {
  brands?: string[];
  sellers?: string[];
  categories?: string[];
  search?: string;
  minPrice?: number | null;
  maxPrice?: number | null;
  sortBy?: string;
  page?: number;
  pageSize?: number;
  // Pages looking for one specific ingredient send false: synonym expansion
  // would bring back the whole category (see expandSearchSynonyms in the backend).
  expandSynonyms?: boolean;
  // The brand PAGE sends true: if the brand sells on its own site, only those
  // products are listed, so a retailer's copy doesn't sit next to it and show
  // the product twice. Brands with no direct products aren't filtered, or
  // brands we only get through retailers would have empty pages. The home
  // page brand filter does NOT use this; there brand and seller stay independent.
  preferBrandStore?: boolean;
}

@Injectable({ providedIn: 'root' })
export class DealsService {
  constructor(private readonly http: HttpClient) {}

  getDeals(query: DealsQuery): Observable<PagedResult<Deal>> {
    return this.http.get<PagedResult<Deal>>(`${API_BASE_URL}/api/deals`, { params: this.buildParams(query) });
  }

  getAllProducts(query: DealsQuery): Observable<PagedResult<Deal>> {
    return this.http.get<PagedResult<Deal>>(`${API_BASE_URL}/api/products`, { params: this.buildParams(query) });
  }

  getStoreDeals(query: DealsQuery): Observable<PagedResult<Deal>> {
    return this.http.get<PagedResult<Deal>>(`${API_BASE_URL}/api/store-deals`, { params: this.buildParams(query) });
  }

  getProductById(id: number): Observable<Deal> {
    return this.http.get<Deal>(`${API_BASE_URL}/api/products/${id}`);
  }

  // The home page's live scan strip, once per page load.
  getStats(): Observable<HomepageStats> {
    return this.http.get<HomepageStats>(`${API_BASE_URL}/api/stats`);
  }

  // The home page's "popular with shoppers" strip. The order is computed on
  // the server from real watchlist and click counts; the client keeps it.
  getPreferredProducts(count = 60): Observable<Deal[]> {
    return this.http.get<Deal[]>(`${API_BASE_URL}/api/preferred-products`, {
      params: new HttpParams().set('count', count),
    });
  }

  // The brand page's overview section: original statistics from our own
  // data instead of the brand's copied history. With a category, the numbers
  // come only from the brand's products in that category (brand x category pages).
  getBrandStats(brand: string, category?: string): Observable<BrandStats> {
    let params = new HttpParams().set('brand', brand);
    if (category) params = params.set('category', category);
    return this.http.get<BrandStats>(`${API_BASE_URL}/api/brand-stats`, { params });
  }

  // The review page's "how it sits in its category" section. The backend
  // returns 404 when the category has no active products; the component then
  // hides the section, it is not treated as an error.
  getCategoryPriceStats(category: string): Observable<CategoryPriceStats | null> {
    const params = new HttpParams().set('category', category);
    return this.http
      .get<CategoryPriceStats>(`${API_BASE_URL}/api/category-price-stats`, { params })
      .pipe(catchError(() => of(null)));
  }

  // The protein calculator's "best value per serving" table. The backend does
  // the math and returns only the top N; fetching the whole category pushed
  // the SSR output to 451 KB.
  getBestValuePerServing(query: {
    category: string;
    brands?: string[];
    search?: string;
    page?: number;
    pageSize?: number;
  }): Observable<PagedResult<Deal>> {
    let params = new HttpParams().set('category', query.category);
    for (const brand of query.brands ?? []) params = params.append('brands', brand);
    if (query.search) params = params.set('search', query.search);
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    return this.http.get<PagedResult<Deal>>(`${API_BASE_URL}/api/best-value-per-serving`, { params });
  }

  // Brand chips for the calculator table. The general /api/filters list would
  // mislead: a brand with products in the category but no serving data would
  // show an empty table when clicked.
  getBestValueBrands(category: string): Observable<string[]> {
    const params = new HttpParams().set('category', category);
    return this.http.get<string[]>(`${API_BASE_URL}/api/best-value-brands`, { params });
  }

  // Brand x category pages, only for pairs that really have products. The
  // sitemap and in-page links both use this, so no page or link is made for
  // an empty combination.
  getBrandCategoryPairs(): Observable<BrandCategoryPair[]> {
    return this.http.get<BrandCategoryPair[]>(`${API_BASE_URL}/api/brand-category-pairs`);
  }

  // Product counts for the brand directory, with the same definition as the
  // brand page (active brand, product not stale).
  getBrandProductCounts(): Observable<BrandProductCount[]> {
    return this.http.get<BrandProductCount[]>(`${API_BASE_URL}/api/brand-product-counts`);
  }

  // Batched request for the product cards' mini sparklines: one call per
  // page (24 cards) instead of one per card (N+1).
  getSparklines(ids: number[], days = 30): Observable<ProductSparkline[]> {
    if (ids.length === 0) return of([]);
    let params = new HttpParams().set('days', days);
    for (const id of ids) params = params.append('ids', id);
    return this.http.get<ProductSparkline[]>(`${API_BASE_URL}/api/products/sparklines`, { params });
  }

  // The header, home, brand and category pages each called this on start (four
  // /api/filters requests per page load). The brand/category list hardly
  // changes, so one request is shared and cached. On error the cache resets so
  // the next call really retries (shareReplay would replay the error forever).
  private filterOptions$: Observable<FilterOptions> | null = null;

  getFilterOptions(): Observable<FilterOptions> {
    if (!this.filterOptions$) {
      this.filterOptions$ = this.http.get<FilterOptions>(`${API_BASE_URL}/api/filters`).pipe(
        catchError((err) => {
          this.filterOptions$ = null;
          return throwError(() => err);
        }),
        shareReplay(1),
      );
    }
    return this.filterOptions$;
  }

  private buildParams(query: DealsQuery): HttpParams {
    let params = new HttpParams();

    for (const brand of query.brands ?? []) {
      params = params.append('brands', brand);
    }
    for (const seller of query.sellers ?? []) {
      params = params.append('sellers', seller);
    }
    for (const category of query.categories ?? []) {
      params = params.append('categories', category);
    }
    if (query.search) params = params.set('search', query.search);
    if (query.minPrice != null) params = params.set('minPrice', query.minPrice);
    if (query.maxPrice != null) params = params.set('maxPrice', query.maxPrice);
    if (query.sortBy) params = params.set('sortBy', query.sortBy);
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    // Only sent when explicitly false; the backend default is true.
    if (query.expandSynonyms === false) params = params.set('expandSynonyms', 'false');
    // Only sent when explicitly requested; the backend default is false.
    if (query.preferBrandStore) params = params.set('preferBrandStore', 'true');

    return params;
  }
}
