import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners, isDevMode } from '@angular/core';
import { PreloadAllModules, RouteReuseStrategy, provideRouter, withPreloading } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideClientHydration, withIncrementalHydration } from '@angular/platform-browser';

import { routes } from './app.routes';
import { MARKET } from './core/market';
import { DealsRouteReuseStrategy } from './core/deals-route-reuse.strategy';
import { provideServiceWorker } from '@angular/service-worker';
import { internalApiInterceptor } from './core/internal-api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Lazy routes (app.routes.ts) aren't downloaded on first load, but with
    // PreloadAllModules they're all fetched in the background once the home
    // page is idle: no extra network wait on click, just a smaller first load.
    provideRouter(routes, withPreloading(PreloadAllModules)),
    // The interceptor is a no-op in the browser; the internal address is only
    // provided in the server config (see core/internal-api.ts).
    provideHttpClient(withFetch(), withInterceptors([internalApiInterceptor])),
    // Incremental hydration: blocks marked @defer (hydrate on ...) still
    // render on the server (search engines see them in the HTML) but aren't
    // hydrated in the browser until their trigger fires. Hydrating
    // off-screen blocks up front caused a long blocking time on first load.
    // Event replay comes with it: a click on a not-yet-hydrated block isn't
    // lost, it's applied once hydration finishes.
    provideClientHydration(withIncrementalHydration()),
    { provide: LOCALE_ID, useValue: MARKET.locale },
    { provide: RouteReuseStrategy, useClass: DealsRouteReuseStrategy },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
