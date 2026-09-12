import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';

export type CookieConsentStatus = 'accepted' | 'rejected' | null;

const STORAGE_KEY = 'cookie-consent';

// The site sets no advertising or analytics cookies today, so this choice
// doesn't block or allow anything yet; the plumbing is in place BEFORE any ad
// or analytics script. When one is added, loading it will depend on
// `status() === 'accepted'` without touching the banner.
@Injectable({ providedIn: 'root' })
export class CookieConsentService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly status = signal<CookieConsentStatus>(this.readStoredStatus());

  accept(): void {
    this.setStatus('accepted');
  }

  reject(): void {
    this.setStatus('rejected');
  }

  private setStatus(status: 'accepted' | 'rejected'): void {
    this.status.set(status);
    if (this.isBrowser) {
      localStorage.setItem(STORAGE_KEY, status);
    }
  }

  private readStoredStatus(): CookieConsentStatus {
    if (!this.isBrowser) return null;
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'accepted' || stored === 'rejected' ? stored : null;
  }
}
