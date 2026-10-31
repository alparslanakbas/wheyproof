import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, inject } from '@angular/core';
import { MonoTypeOperatorFunction, retry, throwError, timer } from 'rxjs';

import { API_BASE_URL } from './api.config';

// SERVER-SIDE RENDERING TALKS TO THE API OVER THE DOCKER NETWORK.
//
// Every server-rendered page used to call the API at its public address: the
// request left the VM, went through Cloudflare and came back to the same VM.
// These internal calls were most of the access log (User-Agent "node", source
// the VM itself); on 13 Sept each external request caused ~5.8 of them. Under
// an attack that multiplier lands on Cloudflare's limits as our own traffic,
// and every page pays an extra TLS round trip. Inside Docker the backend is
// reachable directly at http://wheyproof-backend:8080.
//
// The URL is rewritten ONLY on the server (the token is provided only in
// app.config.server.ts and server.ts); browsers still use the public address.
// Without the environment variable (local development) nothing changes.
export const INTERNAL_API_BASE_URL = new InjectionToken<string | null>('INTERNAL_API_BASE_URL', {
  factory: () => null,
});

// The backend's output cache key includes the scheme. Internal requests are
// plain http, so without this header they would never hit the warmed (https)
// entries, and UseHttpsRedirection would redirect them. Host is no longer part
// of the key (Node's fetch won't send a custom Host header, measured).
export const INTERNAL_API_HEADERS: Readonly<Record<string, string>> = { 'X-Forwarded-Proto': 'https' };

export function toInternalApiUrl(url: string, internalBase: string | null | undefined): string {
  if (!internalBase) return url;
  if (url === API_BASE_URL || url.startsWith(`${API_BASE_URL}/`)) {
    return internalBase.replace(/\/+$/, '') + url.slice(API_BASE_URL.length);
  }
  return url;
}

// RETRYING CONNECTION ERRORS DURING A DEPLOY.
//
// A deploy recreates the backend; for those few seconds its address refuses
// connections and SSR answers the page with a deliberate 503. Caddy's wait
// (lb_try_duration) doesn't cover this path, because SSR reaches the backend
// without going through Caddy: within ±60 s of the 30 Oct deploy, 3 of 49
// requests got a 503 in 19-34 ms (the Turkish site, with this retry, 0 of 83).
//
// Only CONNECTION errors (status 0, no response at all) and only GETs are
// retried; a 4xx/5xx from the backend is a real answer and isn't repeated.
// The ceiling is 15 s: Docker stops the old container within 10 s at most and
// the backend starts listening in 3.8 s (measured). If the backend really is
// down the page still gets a 503, 15 s late; an outage isn't hidden. apiFetch
// in server.ts uses the same limit.
export const CONNECTION_RETRY = { count: 30, delayMs: 500 } as const;

export function retryOnConnectionError<T>(
  count: number = CONNECTION_RETRY.count,
  delayMs: number = CONNECTION_RETRY.delayMs,
): MonoTypeOperatorFunction<T> {
  return retry({
    count,
    delay: (error) => (error instanceof HttpErrorResponse && error.status === 0 ? timer(delayMs) : throwError(() => error)),
  });
}

// A narrower test for apiFetch in server.ts (Node fetch): only errors where the
// connection was NEVER established, so the request never reached the backend.
// /go increments the click counter; repeating a request that broke halfway
// could count a click twice. While the container is gone, Docker's DNS can't
// resolve its name (ENOTFOUND).
const CONNECTION_NOT_ESTABLISHED = new Set(['ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN']);

export function connectionNotEstablished(error: unknown): boolean {
  return CONNECTION_NOT_ESTABLISHED.has((error as { cause?: { code?: string } } | null)?.cause?.code ?? '');
}

export const internalApiInterceptor: HttpInterceptorFn = (req, next) => {
  const internalBase = inject(INTERNAL_API_BASE_URL);
  const url = toInternalApiUrl(req.url, internalBase);
  if (url === req.url) return next(req);
  const response = next(req.clone({ url, setHeaders: INTERNAL_API_HEADERS }));
  return req.method === 'GET' ? response.pipe(retryOnConnectionError()) : response;
};
