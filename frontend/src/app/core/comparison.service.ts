import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';

import { Deal } from './deal.model';

const STORAGE_KEY = 'comparison-slots';

// The minimum kept for a product added to the comparison, so the bottom bar
// can show it across pages without another API call. Not the full Deal:
// fields such as price go stale; the comparison page fetches everything fresh.
export interface ComparisonSlot {
  productId: number;
  productName: string;
  brandName: string;
  imageUrl: string | null;
}

// At most two products: the right number for a readable side-by-side view;
// three columns become unreadable on mobile.
const MAX_SLOTS = 2;

// The selection lives in localStorage, so picking a product on the home page
// and moving to a category page keeps it. The same SSR-safe pattern as
// ThemeService/CookieConsentService.
@Injectable({ providedIn: 'root' })
export class ComparisonService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  // Every component (cards, bottom bar) reads this ONE signal, so a selection
  // made anywhere shows everywhere at once.
  readonly slots = signal<ComparisonSlot[]>(this.readFromStorage());
  readonly isFull = computed(() => this.slots().length >= MAX_SLOTS);

  isSelected(productId: number): boolean {
    return this.slots().some((s) => s.productId === productId);
  }

  // Clicking the same product again removes it (toggle), so nobody has to
  // look for a separate "remove" button.
  toggle(deal: Deal): void {
    const current = this.slots();
    if (current.some((s) => s.productId === deal.productId)) {
      this.write(current.filter((s) => s.productId !== deal.productId));
      return;
    }

    if (current.length >= MAX_SLOTS) return;

    this.write([
      ...current,
      {
        productId: deal.productId,
        productName: deal.productName,
        brandName: deal.brandName,
        imageUrl: deal.imageUrl,
      },
    ]);
  }

  remove(productId: number): void {
    this.write(this.slots().filter((s) => s.productId !== productId));
  }

  clear(): void {
    this.write([]);
  }

  // The comparison page's canonical address. Ids are sorted ASCENDING;
  // otherwise the same content would be reachable at both "29-vs-603" and
  // "603-vs-29" (the same reason brand comparisons are alphabetical).
  static pairSlug(idA: number, idB: number): string {
    const [first, second] = [idA, idB].sort((a, b) => a - b);
    return `${first}-vs-${second}`;
  }

  comparisonUrl(): string | null {
    const current = this.slots();
    if (current.length < MAX_SLOTS) return null;
    return `/compare-products/${ComparisonService.pairSlug(current[0].productId, current[1].productId)}`;
  }

  private write(slots: ComparisonSlot[]): void {
    this.slots.set(slots);
    if (!this.isBrowser) return;

    try {
      if (slots.length === 0) localStorage.removeItem(STORAGE_KEY);
      else localStorage.setItem(STORAGE_KEY, JSON.stringify(slots));
    } catch {
      // Storage may be full or disabled; the selection still lives in the
      // signal for this session, so don't throw.
    }
  }

  private readFromStorage(): ComparisonSlot[] {
    if (!this.isBrowser) return [];

    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];

      const parsed = JSON.parse(raw) as ComparisonSlot[];
      // Guard against broken or old data: keep only records with the expected
      // fields, and at most two of them.
      return Array.isArray(parsed)
        ? parsed.filter((s) => typeof s?.productId === 'number' && typeof s?.productName === 'string').slice(0, MAX_SLOTS)
        : [];
    } catch {
      return [];
    }
  }
}
