import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { FavoritesService } from './favorites.service';

// Born from a real production problem: three components showing the badge
// (site-header, mobile-tab-bar, deals-list) each called list() for the count.
// The count was shared at service level but the REQUEST wasn't; one page
// load sent 2-3 requests and hit the rate limit.

describe('FavoritesService.ensureCount', () => {
  let service: FavoritesService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // The service checks isPlatformBrowser; without a browser it sends nothing.
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    service = TestBed.inject(FavoritesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('sends no request without a token and resets the count', () => {
    service.count.set(5);
    service.ensureCount();

    http.expectNone((r) => r.url.includes('/api/favorites'));
    expect(service.count()).toBe(0);
  });

  it('REGRESSION: sends ONE request even when called several times in a row', () => {
    localStorage.setItem('favorites-token', 'test-token');

    // The three components' ngOnInit run in the same tick; unless the flag is
    // set before subscribe, all three would send a request.
    service.ensureCount();
    service.ensureCount();
    service.ensureCount();

    const requests = http.match((r) => r.url.includes('/api/favorites'));
    expect(requests.length).toBe(1);
    requests[0].flush([{ productId: 1 }, { productId: 2 }]);
    expect(service.count()).toBe(2);
  });

  it("doesn't hit the network again after a successful request", () => {
    localStorage.setItem('favorites-token', 'test-token');

    service.ensureCount();
    http.match((r) => r.url.includes('/api/favorites'))[0].flush([{ productId: 1 }]);

    service.ensureCount();
    http.expectNone((r) => r.url.includes('/api/favorites'));
    expect(service.count()).toBe(1);
  });

  it('retries on the next call after a failed request', () => {
    localStorage.setItem('favorites-token', 'test-token');

    service.ensureCount();
    http
      .match((r) => r.url.includes('/api/favorites'))[0]
      .flush('error', { status: 500, statusText: 'Server Error' });

    // The flag must be reset, or the count would never load for the session.
    service.ensureCount();
    expect(http.match((r) => r.url.includes('/api/favorites')).length).toBe(1);
  });

  it('can load the count again after signOut', () => {
    localStorage.setItem('favorites-token', 'test-token');
    service.ensureCount();
    http.match((r) => r.url.includes('/api/favorites'))[0].flush([{ productId: 1 }]);

    service.signOut();
    expect(service.count()).toBe(0);

    // When a recovery link brings a new token, the request must go out again.
    service.saveToken('recovered-token');
    service.ensureCount();
    expect(http.match((r) => r.url.includes('/api/favorites')).length).toBe(1);
  });
});
