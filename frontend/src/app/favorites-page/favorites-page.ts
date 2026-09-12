import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Meta } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { Deal } from '../core/deal.model';
import { productPath, shouldHandleInApp } from '../core/product-link';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { FavoritesService } from '../core/favorites.service';
import { LoadErrorInfo, describeLoadError, friendlyErrorMessage } from '../core/friendly-error-message';
import { PageMetaService } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { PriceHistoryService } from '../core/price-history.service';
import { formatRelativeTime } from '../core/relative-time';
import { SITE_NAME } from '../core/site-identity';
import { ProductModal } from '../product-modal/product-modal';
import { SiteHeader } from '../site-header/site-header';

// How many times a rate-limited load retries silently. More than two makes no
// sense: if the problem isn't temporary, showing it is more honest.
const MAX_AUTO_RETRY = 2;

@Component({
  selector: 'app-favorites-page',
  imports: [PricePipe, RouterLink, ProductModal, SiteHeader, FormsModule],
  templateUrl: './favorites-page.html',
  styleUrl: './favorites-page.css',
})
export class FavoritesPage implements OnInit, OnDestroy {
  protected readonly displayName = displayName;
  private readonly favoritesService = inject(FavoritesService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly pageMeta = inject(PageMetaService);
  private readonly metaService = inject(Meta);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly dealsService = inject(DealsService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private autoRetryCount = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly favorites = signal<Deal[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<LoadErrorInfo | null>(null);
  protected readonly hasToken = signal(false);
  protected readonly discountedCount = computed(() => this.favorites().filter((deal) => deal.discountPercent > 0).length);
  protected readonly lowCount = computed(() => this.favorites().filter((deal) => deal.isAtThirtyDayLow).length);
  protected readonly opportunityCount = computed(
    () => this.favorites().filter((deal) => deal.discountPercent > 0 || deal.isAtThirtyDayLow).length,
  );
  protected readonly normalCount = computed(() => this.favorites().length - this.opportunityCount());
  protected readonly opportunityRate = computed(() => {
    const total = this.favorites().length;
    return total === 0 ? 0 : Math.round((this.opportunityCount() / total) * 100);
  });

  // See category-page.ts for the same reasoning.
  protected readonly selectedDeal = signal<Deal | null>(null);

  // Watchlist recovery: without a token on this device, an email address
  // requests a link (see FavoritesService.recover and the backend's
  // FavoriteService.SendRecoveryEmailAsync).
  protected readonly recoverEmail = signal('');
  protected readonly recoverSubmitting = signal(false);
  protected readonly recoverStatusMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `My Watchlist | ${SITE_NAME}`,
      description: 'Follow the current prices and price drops of the products on your watchlist.',
      canonicalPath: '/watchlist',
    });
    // Personal content (tied to a localStorage token) that would look
    // empty to crawlers, so noindex.
    this.metaService.updateTag({ name: 'robots', content: 'noindex' });

    // The recovery link from the email (?recover=TOKEN): the token is saved on
    // this device and removed from the URL (so it doesn't sit in history or
    // get shared). Only the first load matters, hence the snapshot.
    // CRITICAL: browser only. router.navigate() during SSR becomes a real
    // HTTP 302, the browser is redirected before any JS runs, and saveToken()
    // is a no-op on the server, so the token would be lost. A real user test
    // found it; a local SSR-less dev server never showed it.
    const recoverToken = this.route.snapshot.queryParamMap.get('recover');
    if (recoverToken && this.isBrowser) {
      this.favoritesService.saveToken(recoverToken);
      this.router.navigate([], { relativeTo: this.route, queryParams: { recover: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }

    this.hasToken.set(!!this.favoritesService.getToken());
    this.loadFavorites();

    this.route.queryParamMap.subscribe((params) => {
      const idParam = params.get('product');
      if (!idParam) {
        this.selectedDeal.set(null);
        return;
      }

      const id = Number(idParam);
      const alreadyLoaded = this.favorites().find((d) => d.productId === id);
      if (alreadyLoaded) {
        this.selectedDeal.set(alreadyLoaded);
        return;
      }

      this.dealsService.getProductById(id).subscribe({
        next: (deal) => this.selectedDeal.set(deal),
        error: () => this.selectedDeal.set(null),
      });
    });
  }

  private loadFavorites(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.favoritesService.list().subscribe({
      next: (deals) => {
        this.favorites.set(deals);
        this.loadError.set(null);
        this.loading.set(false);
        this.autoRetryCount = 0;
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.favoritesService.signOut();
          this.hasToken.set(false);
          this.favorites.set([]);
          this.recoverStatusMessage.set(
            'The list link on this device is no longer valid. You can reopen your list with your email.',
          );
          this.loading.set(false);
          return;
        }

        // 429 = rate limit (Cloudflare). NOT a lasting failure; it clears in a
        // few seconds, and a "connection problem" screen would mislead. Stay
        // in the loading state and retry quietly after a short wait.
        //
        // The Retry-After header may not be exposed to JS on a cross-origin
        // response (Access-Control-Expose-Headers), so it falls back to the
        // observed value (10 s).
        if (error instanceof HttpErrorResponse && error.status === 429 && this.autoRetryCount < MAX_AUTO_RETRY) {
          this.autoRetryCount++;
          const retryAfter = Number(error.headers?.get('Retry-After'));
          const waitMs = (Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 10) * 1000;
          this.clearRetryTimer();
          this.retryTimer = setTimeout(() => this.loadFavorites(), waitMs);
          return;
        }

        this.loadError.set(describeLoadError(error));
        this.loading.set(false);
      },
    });
  }

  protected retryLoad(): void {
    // A deliberate click resets the automatic retry allowance.
    this.autoRetryCount = 0;
    this.clearRetryTimer();
    this.loadFavorites();
  }

  private clearRetryTimer(): void {
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
  }

  // Cancel a pending retry when leaving the page; otherwise a needless
  // request goes out while the visitor is elsewhere.
  ngOnDestroy(): void {
    this.clearRetryTimer();
  }

  // Detaches the list from this browser only; the watchlist stays on the
  // server and the recovery link brings it back. The page returns to the
  // "we'll email you a link" form shown to visitors without a token.
  protected signOut(): void {
    this.favoritesService.signOut();
    this.hasToken.set(false);
    this.favorites.set([]);
    this.recoverStatusMessage.set(null);
  }

  protected submitRecover(): void {
    const email = this.recoverEmail().trim();
    if (!email) return;

    this.recoverSubmitting.set(true);
    this.favoritesService.recover(email).subscribe({
      next: (result) => {
        this.recoverStatusMessage.set(result.message);
        this.recoverSubmitting.set(false);
      },
      error: (err) => {
        this.recoverStatusMessage.set(friendlyErrorMessage(err));
        this.recoverSubmitting.set(false);
      },
    });
  }

  protected removeFavorite(deal: Deal): void {
    this.favoritesService.remove(deal.productId).subscribe(() => {
      this.favorites.update((list) => list.filter((d) => d.productId !== deal.productId));
    });
  }

  // Row links must be real <a href> (see core/product-link.ts). Here the
  // modal opens through ?product= without leaving the page.
  protected productPath(deal: Deal): string {
    return productPath(deal);
  }

  protected onProductClick(event: MouseEvent, deal: Deal): void {
    // The row may have its own click handler; don't fire twice.
    event.stopPropagation();
    if (!shouldHandleInApp(event)) return;
    event.preventDefault();
    this.openDeal(deal);
  }

  protected openDeal(deal: Deal): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: { product: deal.productId }, queryParamsHandling: 'merge' });
  }

  protected closeDeal(): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: { product: null }, queryParamsHandling: 'merge' });
  }

  protected lastCheckedText(deal: Deal): string {
    return formatRelativeTime(deal.scrapedAt);
  }

  protected goToStoreUrl(deal: Deal): string {
    return this.priceHistoryService.goToStoreUrl(deal.productId, deal.storeUrl);
  }

  /** Counts the store click; the link goes straight to the store, so /go/{id}
   *  can no longer count it (see PriceHistoryService). */
  protected trackStoreClick(productId: number): void {
    this.priceHistoryService.trackStoreClick(productId);
  }
}
