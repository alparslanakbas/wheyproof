import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter } from 'rxjs';

// When a new deploy goes live, tabs already open on the site don't notice:
// even with the service worker downloading the new version in the
// background, people kept browsing on the old JS until they reloaded. This
// service turns the `VERSION_READY` event into a signal, and the banner shows
// a "reload" button from it.
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly swUpdate = inject(SwUpdate);

  readonly updateAvailable = signal(false);

  constructor() {
    if (!this.isBrowser || !this.swUpdate.isEnabled) return;

    this.swUpdate.versionUpdates
      .pipe(filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'))
      .subscribe(() => {
        this.updateAvailable.set(true);
        // Even if the banner is never seen or clicked, the new service worker
        // is activated in the background right away (WITHOUT reloading). It
        // then takes over the open tab (clients.claim()), so even with old JS
        // the NEXT network request (e.g. a "Go to store" link) goes through
        // the fixed service worker. A critical fix reaches open tabs without
        // waiting for anyone to click.
        this.swUpdate.activateUpdate().catch(() => undefined);
      });

    // registerWhenStable:30000 only delays the FIRST registration; the service
    // worker doesn't look for new versions on its own, so a tab left open is
    // never checked. Check once on load, then every 10 minutes (cheap: it only
    // checks ngsw.json's ETag).
    this.swUpdate.checkForUpdate().catch(() => undefined);
    setInterval(() => this.swUpdate.checkForUpdate().catch(() => undefined), 10 * 60 * 1000);
  }

  // location.reload() alone is NOT enough: the new service worker stays
  // "waiting" and the page is still controlled by the OLD one.
  // activateUpdate() tells the new worker to take over (SKIP_WAITING), and
  // the reload must come AFTER it; otherwise "reload" changed nothing (a real
  // production bug).
  reload(): void {
    if (!this.isBrowser) return;
    this.swUpdate
      .activateUpdate()
      .catch(() => undefined)
      .finally(() => window.location.reload());
  }
}
