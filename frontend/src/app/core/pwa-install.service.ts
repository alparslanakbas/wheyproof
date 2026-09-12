import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';

const DISMISS_KEY = 'pwa-install-dismissed';

// The standard DOM types don't know BeforeInstallPromptEvent (Chromium only
// for now), so a narrow interface is declared here.
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

// "Add to home screen" hint. The `beforeinstallprompt` event is captured and
// kept so the site can show its own strip instead of the browser's mini
// infobar. The event ONLY fires in Chromium browsers (Android Chrome
// included) once the site meets PWA criteria (manifest + service worker); iOS
// Safari never fires it, so `canInstall` is only true where it is supported,
// with no platform check needed.
@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private deferredPrompt: BeforeInstallPromptEvent | null = null;

  readonly canInstall = signal(false);

  constructor() {
    if (!this.isBrowser || localStorage.getItem(DISMISS_KEY)) return;

    window.addEventListener('beforeinstallprompt', (event) => {
      event.preventDefault();
      this.deferredPrompt = event as BeforeInstallPromptEvent;
      this.canInstall.set(true);
    });

    window.addEventListener('appinstalled', () => {
      this.deferredPrompt = null;
      this.canInstall.set(false);
    });
  }

  async promptInstall(): Promise<void> {
    if (!this.deferredPrompt) return;
    await this.deferredPrompt.prompt();
    await this.deferredPrompt.userChoice;
    this.deferredPrompt = null;
    this.canInstall.set(false);
  }

  dismiss(): void {
    this.canInstall.set(false);
    if (this.isBrowser) localStorage.setItem(DISMISS_KEY, '1');
  }
}
