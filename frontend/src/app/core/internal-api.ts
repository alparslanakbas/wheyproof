import { HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, inject } from '@angular/core';

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

export const internalApiInterceptor: HttpInterceptorFn = (req, next) => {
  const internalBase = inject(INTERNAL_API_BASE_URL);
  const url = toInternalApiUrl(req.url, internalBase);
  if (url === req.url) return next(req);
  return next(req.clone({ url, setHeaders: INTERNAL_API_HEADERS }));
};
