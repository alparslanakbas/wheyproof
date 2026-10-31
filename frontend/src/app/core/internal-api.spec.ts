import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { defer, firstValueFrom, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { API_BASE_URL } from './api.config';
import {
  CONNECTION_RETRY,
  INTERNAL_API_BASE_URL,
  connectionNotEstablished,
  internalApiInterceptor,
  retryOnConnectionError,
  toInternalApiUrl,
} from './internal-api';

describe('toInternalApiUrl', () => {
  it('leaves the URL alone without an internal base (local dev, browser)', () => {
    expect(toInternalApiUrl(`${API_BASE_URL}/api/deals`, null)).toBe(`${API_BASE_URL}/api/deals`);
  });

  it('swaps the API prefix and keeps path and query', () => {
    expect(toInternalApiUrl(`${API_BASE_URL}/api/deals?page=2`, 'http://wheyproof-backend:8080/')).toBe(
      'http://wheyproof-backend:8080/api/deals?page=2',
    );
  });

  it('ignores other hosts, look-alike prefixes and relative URLs', () => {
    const base = 'http://wheyproof-backend:8080';
    expect(toInternalApiUrl('https://example.com/api/deals', base)).toBe('https://example.com/api/deals');
    expect(toInternalApiUrl(`${API_BASE_URL}.evil.test/x`, base)).toBe(`${API_BASE_URL}.evil.test/x`);
    expect(toInternalApiUrl('/admin/api/status', base)).toBe('/admin/api/status');
  });
});

// During a deploy the backend refuses connections for a few seconds (31 Oct).
describe('retryOnConnectionError', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const connectionError = () => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' });

  it('delivers the response once the backend is back', async () => {
    let attempts = 0;
    const source = defer(() => (++attempts <= 2 ? throwError(connectionError) : of('data')));

    const result = firstValueFrom(source.pipe(retryOnConnectionError(5, 500)));
    await vi.advanceTimersByTimeAsync(1000);

    await expect(result).resolves.toBe('data');
    expect(attempts).toBe(3);
  });

  it("doesn't retry a real answer from the backend (500)", async () => {
    let attempts = 0;
    const source = defer(() => {
      attempts++;
      return throwError(() => new HttpErrorResponse({ status: 500 }));
    });

    await expect(firstValueFrom(source.pipe(retryOnConnectionError(5, 500)))).rejects.toMatchObject({ status: 500 });
    expect(attempts).toBe(1);
  });

  it('gives up at the ceiling when the backend never returns (an outage is not hidden)', async () => {
    let attempts = 0;
    const source = defer(() => {
      attempts++;
      return throwError(connectionError);
    });

    const result = firstValueFrom(source.pipe(retryOnConnectionError(3, 500)));
    const expected = expect(result).rejects.toMatchObject({ status: 0 });
    await vi.advanceTimersByTimeAsync(1500);

    await expected;
    expect(attempts).toBe(4);
  });

  it('the production ceiling is ~15 s (Docker stops within 10 s + 3.8 s startup)', () => {
    expect(CONNECTION_RETRY.count * CONNECTION_RETRY.delayMs).toBe(15_000);
  });
});

describe('connectionNotEstablished (apiFetch in server.ts)', () => {
  const fetchError = (code: string) => new TypeError('fetch failed', { cause: Object.assign(new Error(code), { code }) });

  it('is true when the request never reached the backend', () => {
    expect(connectionNotEstablished(fetchError('ECONNREFUSED'))).toBe(true);
    expect(connectionNotEstablished(fetchError('ENOTFOUND'))).toBe(true);
    expect(connectionNotEstablished(fetchError('EAI_AGAIN'))).toBe(true);
  });

  it('is false for a connection that broke halfway and for other errors (a /go click must not count twice)', () => {
    expect(connectionNotEstablished(fetchError('ECONNRESET'))).toBe(false);
    expect(connectionNotEstablished(new Error('other'))).toBe(false);
    expect(connectionNotEstablished(null)).toBe(false);
  });
});

describe('internalApiInterceptor', () => {
  const internal = 'http://wheyproof-backend:8080';
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([internalApiInterceptor])),
        provideHttpClientTesting(),
        { provide: INTERNAL_API_BASE_URL, useValue: internal },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    vi.useRealTimers();
  });

  it('retries a GET to the internal address with a NEW request after a connection error', async () => {
    const result = firstValueFrom(http.get<{ ok: boolean }>(`${API_BASE_URL}/api/stats`));

    controller.expectOne(`${internal}/api/stats`).error(new ProgressEvent('error'));
    await vi.advanceTimersByTimeAsync(CONNECTION_RETRY.delayMs);
    const second = controller.expectOne(`${internal}/api/stats`);
    expect(second.request.headers.get('X-Forwarded-Proto')).toBe('https');
    second.flush({ ok: true });

    await expect(result).resolves.toEqual({ ok: true });
  });

  it("doesn't retry a POST", async () => {
    const result = firstValueFrom(http.post(`${API_BASE_URL}/api/subscribe`, {}));
    const expected = expect(result).rejects.toMatchObject({ status: 0 });

    controller.expectOne(`${internal}/api/subscribe`).error(new ProgressEvent('error'));
    await vi.advanceTimersByTimeAsync(CONNECTION_RETRY.delayMs * 2);

    await expected;
    controller.expectNone(`${internal}/api/subscribe`);
  });

  it('leaves requests to other addresses alone (the browser path)', async () => {
    const result = firstValueFrom(http.get('https://example.com/x'));
    const expected = expect(result).rejects.toMatchObject({ status: 0 });

    controller.expectOne('https://example.com/x').error(new ProgressEvent('error'));
    await vi.advanceTimersByTimeAsync(CONNECTION_RETRY.delayMs * 2);

    await expected;
    controller.expectNone('https://example.com/x');
  });
});
