import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * API client for the admin panel.
 *
 * <b>API_BASE_URL IS DELIBERATELY NOT USED.</b> The rest of the site calls the
 * api. subdomain; the panel calls `/admin/api/...` on ITS OWN origin, and Caddy
 * forwards that to the backend. Reason: Cloudflare Access protects a host +
 * path. Had the panel fetched its data from the api. subdomain, those requests
 * would sit OUTSIDE Access: the page protected, the data itself not. Being
 * same-origin also means the session cookie goes along by itself and CORS
 * isn't needed (CORS is deliberately off in this project).
 */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/admin/api';

  signIn(key: string): Observable<{ ok: boolean }> {
    return this.http.post<{ ok: boolean }>(`${this.base}/session`, { key });
  }

  signOut(): Observable<{ ok: boolean }> {
    return this.http.delete<{ ok: boolean }>(`${this.base}/session`);
  }

  status(): Observable<AdminStatus> {
    return this.http.get<AdminStatus>(`${this.base}/status`);
  }

  securityEvents(days: number, kind: string | null): Observable<SecurityEventsResponse> {
    let path = `${this.base}/security-events?days=${days}`;
    if (kind) path += `&kind=${encodeURIComponent(kind)}`;
    return this.http.get<SecurityEventsResponse>(path);
  }

  /**
   * Failed admin operations. SEPARATE from the events endpoint because the
   * two lists answer different questions: "who is trying what from outside"
   * and "why didn't my action work".
   */
  adminFailures(days: number): Observable<AdminFailure[]> {
    return this.http.get<AdminFailure[]>(`${this.base}/admin-failures?days=${days}`);
  }

  coupons(): Observable<Coupon[]> {
    return this.http.get<Coupon[]>(`${this.base}/coupons`);
  }

  createCoupon(coupon: CreateCouponRequest): Observable<unknown> {
    return this.http.post(`${this.base}/coupons`, coupon);
  }

  updateCoupon(id: number, coupon: UpdateCouponRequest): Observable<unknown> {
    return this.http.put(`${this.base}/coupons/${id}`, coupon);
  }

  brands(): Observable<AdminBrand[]> {
    return this.http.get<AdminBrand[]>(`${this.base}/brands`);
  }

  setBrandActive(id: number, isActive: boolean): Observable<VisibilityUpdate> {
    return this.http.put<VisibilityUpdate>(`${this.base}/brands/${id}`, { isActive });
  }

  /** One page of products; `total` counts every match, not just this page. */
  products(
    search: string,
    hiddenOnly: boolean,
    missingNutrition = false,
    uncategorised = false,
    page = 1,
  ): Observable<AdminProductPage> {
    const params = new URLSearchParams();
    if (search.trim()) params.set('search', search.trim());
    if (hiddenOnly) params.set('hiddenOnly', 'true');
    if (missingNutrition) params.set('missingNutrition', 'true');
    if (uncategorised) params.set('uncategorised', 'true');
    if (page > 1) params.set('page', String(page));
    const query = params.toString();
    return this.http.get<AdminProductPage>(`${this.base}/products${query ? `?${query}` : ''}`);
  }

  setProductActive(id: number, isActive: boolean): Observable<VisibilityUpdate> {
    return this.http.put<VisibilityUpdate>(`${this.base}/products/${id}`, { isActive });
  }

  /** A category slug, or null to go back to the automatic category. Applies to every size of the page. */
  setProductCategory(id: number, category: string | null): Observable<ManualEditResponse> {
    return this.http.put<ManualEditResponse>(`${this.base}/products/${id}/category`, { category });
  }

  /** Nutrition from the brand's label; refused with a reason when calories don't match the macros. */
  setProductNutrition(id: number, nutrition: ManualNutrition): Observable<ManualEditResponse> {
    return this.http.put<ManualEditResponse>(`${this.base}/products/${id}/nutrition`, nutrition);
  }

  clearProductNutrition(id: number): Observable<ManualEditResponse> {
    return this.http.delete<ManualEditResponse>(`${this.base}/products/${id}/nutrition`);
  }

  subscribers(): Observable<SubscribersResponse> {
    return this.http.get<SubscribersResponse>(`${this.base}/subscribers`);
  }

  deactivateSubscriber(id: number): Observable<unknown> {
    return this.http.post(`${this.base}/subscribers/${id}/deactivate`, {});
  }

  /**
   * There is deliberately no "activate": with double opt-in only the person
   * can turn a subscription on. The panel can only resend the email.
   */
  sendSubscriberConfirmation(id: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/subscribers/${id}/send-confirmation`, {});
  }
}

export type SubscriberStatus = 'active' | 'pending' | 'unsubscribed';

export interface AdminSubscriber {
  id: number;
  email: string;
  status: SubscriberStatus;
  subscribedAt: string;
  confirmedAt: string | null;
  unsubscribedAt: string | null;
  lastConfirmationEmailSentAt: string | null;
  lastDigestSentAt: string | null;
  watchCount: number;
  favoriteCount: number;
}

export interface SubscribersResponse {
  subscribers: AdminSubscriber[];
  summary: { total: number; active: number; pending: number; unsubscribed: number };
}

export interface AdminStatus {
  products: { total: number; withNutrition: number; brandCount: number };
  clickTotal: number;
  nutrition: { lastRun: string | null; nextRun: string | null };
  subscribers: { confirmed: number; pending: number };
  sources: { source: string; lastScraped: string | null }[];
  lastDayEvents: { kind: string; count: number }[];
}

export interface SecurityEvent {
  id: number;
  occurredAt: string;
  ip: string;
  kind: string;
  method: string;
  path: string;
  statusCode: number;
  userAgent: string | null;
  country: string | null;
}

export interface SecurityEventsResponse {
  events: SecurityEvent[];
  summary: { kind: string; count: number }[];
  topIps: { ip: string; count: number; firstSeen: string; lastSeen: string }[];
}

export interface AdminFailure {
  id: number;
  occurredAt: string;
  method: string;
  path: string;
  statusCode: number;
  /** The endpoint's own response or the exception text; null if the endpoint returned no body. */
  reason: string | null;
  ip: string | null;
}

export interface Coupon {
  id: number;
  code: string | null;
  description: string;
  brandId: number | null;
  brandName: string | null;
  seller: string | null;
  validUntil: string | null;
  lastVerifiedAt: string;
  isActive: boolean;
}

// Create and update have DIFFERENT SHAPES, matching the backend exactly:
// create takes the brand by NAME (not id) and exactly ONE of brand/seller
// may be set (also enforced by a check constraint in the database); update
// never changes the target, only the content and status.
export interface CreateCouponRequest {
  brandName: string | null;
  seller: string | null;
  code: string | null;
  description: string;
  validUntil: string | null;
}

export interface UpdateCouponRequest {
  code: string | null;
  description: string;
  validUntil: string | null;
  /**
   * When omitted the coupon's status DOESN'T CHANGE; the backend only
   * updates the fields it receives. Editing omits it on purpose: fixing the
   * text mustn't accidentally publish an inactive coupon.
   */
  isActive?: boolean;
  /**
   * To REMOVE the end date. Sending `validUntil: null` isn't enough: on this
   * endpoint null means "leave this field alone", not "clear it".
   */
  clearValidUntil?: boolean;
}

export interface AdminBrand {
  id: number;
  name: string;
  isActive: boolean;
  productCount: number;
  hiddenProducts: number;
}

export interface AdminProduct {
  id: number;
  name: string;
  brand: string;
  seller: string | null;
  isActive: boolean;
  latestPrice: number | null;
  category: string | null;
  categoryIsManual: boolean;
  /** Normalized table, e.g. {"Calories":"160","Protein":"25g"}; null when missing. */
  nutritionJson: string | null;
  nutritionIsManual: boolean;
  servingSizeGrams: number | null;
}

export interface AdminProductPage {
  items: AdminProduct[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ManualNutrition {
  servingSizeGrams: number | null;
  calories: number | null;
  proteinGrams: number | null;
  carbohydrateGrams: number | null;
  fatGrams: number | null;
  fiberGrams: number | null;
  /** Rows beyond the macros, as printed on the label ("Caffeine", 200, "mg"). */
  otherRows: { label: string; amount: number | null; unit: string }[];
}

export interface ManualEditResponse {
  rowsUpdated: number;
}

export interface VisibilityUpdate {
  id: number;
  name: string;
  isActive: boolean;
}
