import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { Observable, of, tap } from 'rxjs';

import { API_BASE_URL } from './api.config';
import { Deal } from './deal.model';

const TOKEN_KEY = 'favorites-token';

// A watchlist with no account or login: the token the backend returns on the
// first add is kept in localStorage and sent with later requests (the same
// SSR-safe pattern as ThemeService/CookieConsentService).
@Injectable({ providedIn: 'root' })
export class FavoritesService {
  private readonly http = inject(HttpClient);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  // Every watchlist badge (site header, mobile tab bar, the home page's own
  // header) reads this ONE signal, kept at service level (singleton) so they
  // all update together without a reload.
  readonly count = signal(0);

  getToken(): string | null {
    return this.isBrowser ? localStorage.getItem(TOKEN_KEY) : null;
  }

  hasToken(): boolean {
    return this.getToken() !== null;
  }

  // Detaches the list from THIS DEVICE; the watchlist stays on the server and
  // can be restored with the recovery link.
  //
  // Without it a saved list could never be removed from that browser: on a
  // shared computer the next person saw the previous person's list with no
  // way of noticing.
  signOut(): void {
    if (this.isBrowser) localStorage.removeItem(TOKEN_KEY);
    this.count.set(0);
    // Reset the flag too, so the count can be fetched again after the list is
    // restored through the recovery link.
    this.countLoaded = false;
  }

  saveToken(token: string | null): void {
    // The token can be null: when the email already belongs to another
    // subscriber, the backend no longer reveals that account's token, and
    // nothing is written to localStorage.
    if (this.isBrowser && token) localStorage.setItem(TOKEN_KEY, token);
  }

  // recoverySent: when the email belongs to an existing subscriber (and this
  // device has no token), the backend sends a recovery email so this device
  // can see that watchlist too.
  // After adding, the count is refreshed with a full list() rather than +1:
  // with recoverySent=true this device had no token (local count 0), but the
  // real count on the server can be more than 1.
  add(productId: number, email?: string): Observable<{ token: string | null; recoverySent: boolean }> {
    return this.http
      .post<{ token: string | null; recoverySent: boolean }>(`${API_BASE_URL}/api/products/${productId}/favorite`, {
        token: this.getToken(),
        email: email ?? null,
      })
      .pipe(tap(() => this.refreshCount()));
  }

  remove(productId: number): Observable<void> {
    return this.http
      .delete<void>(`${API_BASE_URL}/api/products/${productId}/favorite`, {
        params: { token: this.getToken() ?? '' },
      })
      .pipe(tap(() => this.count.update((c) => Math.max(0, c - 1))));
  }

  list(): Observable<Deal[]> {
    const token = this.getToken();
    if (!token) return of([]);
    return this.http.get<Deal[]>(`${API_BASE_URL}/api/favorites`, { params: { token } }).pipe(tap((list) => this.count.set(list.length)));
  }

  // When the token is lost on this device (cleared data, another browser or
  // app), a link carrying the token is emailed. The response is the same
  // whether or not the email is registered (the backend prevents
  // enumeration), so a single message is returned here too.
  recover(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API_BASE_URL}/api/favorites/recover`, { email });
  }

  // The three components showing the badge used to call list() EACH. The
  // count was shared, the REQUEST wasn't: three requests per page load, more
  // than ten after a few navigations and an add, and Cloudflare's rate limit
  // answered 429, which showed as a flickering "couldn't load your watchlist".
  //
  // Now the count is fetched once per app.
  private countLoaded = false;

  ensureCount(): void {
    // The server can't read the token (no localStorage); no request in SSR.
    if (!this.isBrowser) return;

    if (!this.getToken()) {
      this.count.set(0);
      return;
    }

    if (this.countLoaded) return;

    // The flag is set BEFORE subscribing, synchronously. The three
    // components' ngOnInit run in the same tick; setting it later, all three
    // would see "not loaded yet" and send three requests again.
    this.countLoaded = true;
    this.list().subscribe({
      // On failure, clear the flag so the next navigation retries.
      error: () => {
        this.countLoaded = false;
      },
    });
  }

  private refreshCount(): void {
    this.countLoaded = false;
    this.ensureCount();
  }
}
