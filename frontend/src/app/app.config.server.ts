import { mergeApplicationConfig, ApplicationConfig, Provider } from '@angular/core';
import { HTTP_TRANSFER_CACHE_ORIGIN_MAP } from '@angular/common/http';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { API_BASE_URL } from './core/api.config';
import { INTERNAL_API_BASE_URL } from './core/internal-api';

// http://wheyproof-backend:8080 in Docker (docker-compose.yml). Unset locally,
// where SSR keeps calling API_BASE_URL.
const internalApiBase = process.env['API_INTERNAL_URL']?.replace(/\/+$/, '') || null;

// The transfer state key is derived from the request URL. With the server
// calling the internal address and the browser asking for the public one the
// keys wouldn't match, and the browser would download again during hydration
// what the server already fetched. This map stores entries under the public origin.
const internalApiProviders: Provider[] = internalApiBase
  ? [
      { provide: INTERNAL_API_BASE_URL, useValue: internalApiBase },
      { provide: HTTP_TRANSFER_CACHE_ORIGIN_MAP, useValue: { [internalApiBase]: API_BASE_URL } },
    ]
  : [];

const serverConfig: ApplicationConfig = {
  providers: [provideServerRendering(withRoutes(serverRoutes)), ...internalApiProviders],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
