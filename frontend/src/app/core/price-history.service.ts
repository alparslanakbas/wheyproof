import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from './api.config';
import { PriceHistory } from './price-history.model';
import { sitePath } from './site-path';

@Injectable({ providedIn: 'root' })
export class PriceHistoryService {
  private readonly http = inject(HttpClient);

  get(productId: number, days: number): Observable<PriceHistory> {
    return this.http.get<PriceHistory>(`${API_BASE_URL}/api/products/${productId}/price-history`, {
      params: { days },
    });
  }

  /**
   * The "Go to store" link address.
   *
   * With the store address (affiliate code included) at hand, the link goes
   * there DIRECTLY. It used to go through our own /go/{id} endpoint, which
   * redirected with a 302; in an installed PWA that redirect KILLED the back
   * button (the new browsing context only held the redirect chain, and back
   * closed it and left the app).
   *
   * Without an address (a response from an old cache) /go/{id} is the fallback.
   */
  goToStoreUrl(productId: number, storeUrl?: string | null): string {
    return storeUrl ?? sitePath(`/go/${productId}`);
  }

  /**
   * Counts the store click.
   *
   * The link goes straight to the store, so /go/{id} can no longer count it.
   * sendBeacon guarantees the request goes out even as the page leaves, and
   * with an empty body it stays a "simple" request with no CORS preflight (a
   * preflight could be cancelled on unload and lose the count).
   */
  trackStoreClick(productId: number): void {
    if (typeof navigator === 'undefined' || !navigator.sendBeacon) return;
    try {
      navigator.sendBeacon(`${API_BASE_URL}/api/products/${productId}/click`);
    } catch {
      // Losing a count never justifies blocking the trip to the store.
    }
  }
}
