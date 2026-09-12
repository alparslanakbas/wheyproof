import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Component, PLATFORM_ID, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { brandSlug } from '../core/brand-slug';
import { dedupeSameDaySamePrice, hoverAlign, nearestPointIndex, tooltipDateLabel } from '../core/chart-hover';
import { buildProductFacts } from '../core/product-facts';
import { buildProductNarrative } from '../core/product-narrative';
import { canonicalOrigin } from '../core/canonical-link';
import { Deal } from '../core/deal.model';
import { displayName } from '../core/display-name';
import { FavoritesService } from '../core/favorites.service';
import { friendlyErrorMessage } from '../core/friendly-error-message';
import { MARKET } from '../core/market';
import { PricePipe } from '../core/price.pipe';
import { PricePoint } from '../core/price-history.model';
import { PriceHistoryService } from '../core/price-history.service';
import { ProductFeedbackService } from '../core/product-feedback.service';
import { formatRelativeTime } from '../core/relative-time';
import { slugify } from '../core/slugify';
import { buildAreaPath, buildLinePath, toCoordinates } from '../core/spark-chart';
import { WatchService } from '../core/watch.service';
import { ShareButton } from '../share-button/share-button';

interface TimeRangeOption {
  label: string;
  days: number;
  periodName: string;
}

const TIME_RANGES: TimeRangeOption[] = [
  { label: '7D', days: 7, periodName: 'the last 7 days' },
  { label: '15D', days: 15, periodName: 'the last 15 days' },
  { label: '1M', days: 30, periodName: 'the last month' },
  { label: '6M', days: 180, periodName: 'the last 6 months' },
  { label: '1Y', days: 365, periodName: 'the last year' },
];

const CHART_WIDTH = 600;
const CHART_HEIGHT = 220;
const CHART_PADDING_Y = 16;
const AXIS_LABEL_COUNT = 5;

// A fixed zone (MARKET.timeZone) rather than the visitor's device zone or the
// SSR server's: otherwise the same price point could belong to a different
// "day" for different visitors (a scan at 21:10 UTC falls on different dates
// in different zones).
const axisDateFormatter = new Intl.DateTimeFormat(MARKET.locale, { month: 'short', day: 'numeric', timeZone: MARKET.timeZone });

@Component({
  selector: 'app-product-modal',
  imports: [PricePipe, ShareButton, RouterLink, FormsModule],
  templateUrl: './product-modal.html',
})
export class ProductModal {
  // For the brand link: toLowerCase() carried spaces into the address and
  // produced a copy that differed from the canonical.
  protected readonly brandSlug = brandSlug;

  protected readonly displayName = displayName;
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly watchService = inject(WatchService);
  private readonly productFeedbackService = inject(ProductFeedbackService);
  private readonly favoritesService = inject(FavoritesService);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly deal = input.required<Deal>();
  readonly closed = output<void>();

  protected readonly shareUrl = computed(
    () => `${canonicalOrigin(this.document)}/product/${this.deal().productId}/${slugify(this.deal().productName)}`,
  );

  protected readonly reviewLink = computed(() => ['/review', this.deal().productId, slugify(this.deal().productName)]);

  protected readonly timeRanges = TIME_RANGES;
  protected readonly selectedRange = signal<TimeRangeOption>(TIME_RANGES[2]);
  protected readonly lastCheckedText = computed(() => formatRelativeTime(this.deal().scrapedAt));

  // "loading": a request is in flight. "hasData": data arrived at least once.
  // Switching ranges dims the old chart instead of hiding it, so the whole
  // modal doesn't look like it "refreshes" on every click.
  protected readonly loading = signal(true);
  protected readonly hasData = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly points = signal<PricePoint[]>([]);
  protected readonly minPrice = signal(0);
  protected readonly maxPrice = signal(0);
  protected readonly currentPrice = signal(0);
  protected readonly hoverIndex = signal<number | null>(null);

  // "Notify me": a one-time email if this product's price drops on a later check.
  protected readonly watchFormOpen = signal(false);
  protected readonly watchEmail = signal('');
  protected readonly watchSubmitting = signal(false);
  protected readonly watchStatusMessage = signal<string | null>(null);

  // "Was this helpful?": a simple trust signal. With no accounts, repeat
  // votes are blocked with localStorage; there is no dedup on the backend.
  protected readonly votedHelpful = signal<boolean | null>(null);

  // Watchlist: no account needed; the first add asks for an email (the token
  // is saved to localStorage) and later products don't ask again. "favorited"
  // is an optimistic per-session state: it resets on reload, and clicking
  // again is harmless (the backend doesn't add the same item twice).
  protected readonly favorited = signal(false);
  protected readonly favoriteFormOpen = signal(false);
  protected readonly favoriteEmail = signal('');
  protected readonly favoriteSubmitting = signal(false);
  protected readonly favoriteErrorMessage = signal<string | null>(null);
  protected readonly favoriteStatusMessage = signal<string | null>(null);

  protected readonly coordinates = computed(() =>
    toCoordinates(this.points(), this.minPrice(), this.maxPrice(), {
      width: CHART_WIDTH,
      height: CHART_HEIGHT,
      paddingY: CHART_PADDING_Y,
    }),
  );
  protected readonly chartAreaPath = computed(() => buildAreaPath(this.coordinates(), CHART_HEIGHT));
  protected readonly chartLinePath = computed(() => buildLinePath(this.coordinates()));
  protected readonly xAxisLabels = computed(() => this.buildXAxisLabels(this.points()));

  protected readonly hoverInfo = computed(() => {
    const idx = this.hoverIndex();
    if (idx === null) return null;
    const coords = this.coordinates();
    const pts = this.points();
    if (idx >= coords.length || idx >= pts.length) return null;

    const [x, y] = coords[idx];
    // The tooltip moves with the point; centered near an edge it overflowed
    // and was clipped (the modal is overflow-y-auto, which implicitly limits
    // overflow-x too), so near an edge it leans that way.
    const align = hoverAlign(x, CHART_WIDTH);

    return {
      x,
      y,
      align,
      price: pts[idx].price,
      dateLabel: tooltipDateLabel(pts, idx),
    };
  });

  protected readonly savingsText = computed(() => {
    const max = this.maxPrice();
    const current = this.currentPrice();
    if (max <= 0 || current >= max) return null;
    const diff = max - current;
    const percent = Math.round((diff / max) * 100);
    return { diff, percent, periodName: this.selectedRange().periodName };
  });

  // "This product's price dropped N times": an insight instead of raw data.
  // points() already went through dedupeSameDaySamePrice, so counting
  // consecutive real drops is enough. Zero shows nothing rather than a cold
  // "0 times".
  protected readonly discountEventCount = computed(() => {
    const pts = this.points();
    let count = 0;
    for (let i = 1; i < pts.length; i++) {
      if (pts[i].price < pts[i - 1].price) count++;
    }
    return count;
  });

  // The store's own description is NOT shown here; a list built entirely from
  // our own measurements is. See the note in core/product-facts.ts.
  protected readonly productFacts = computed(() =>
    buildProductFacts(this.deal(), this.discountEventCount()),
  );

  // A narrative from our own measurements. The fact list is for scanning;
  // this gives the page its distinctive text (see core/product-narrative.ts).
  protected readonly narrative = computed(() =>
    buildProductNarrative(this.deal(), this.discountEventCount()),
  );

  constructor() {
    effect(() => {
      // Refetch when deal() or selectedRange() changes.
      const deal = this.deal();
      const range = this.selectedRange();
      this.load(deal.productId, range.days);
    });

    // The modal instance is reused across products (route reuse), so the
    // vote state must be read again for each product.
    effect(() => {
      const productId = this.deal().productId;
      if (!this.isBrowser) {
        this.votedHelpful.set(null);
        return;
      }
      const stored = localStorage.getItem(`product-vote-${productId}`);
      this.votedHelpful.set(stored === 'yes' ? true : stored === 'no' ? false : null);
    });

    // "favorited" also resets per deal(); otherwise "On your watchlist" from
    // the previous product would leak into the next one.
    effect(() => {
      this.deal();
      this.favorited.set(false);
      this.favoriteFormOpen.set(false);
      this.favoriteErrorMessage.set(null);
      this.favoriteStatusMessage.set(null);
    });
  }

  // The first and last x-axis labels centered on the very edge overflowed by
  // half and were cut (noticeably on narrow mobile screens), so the end
  // labels lean to the edge and the middle ones stay centered.
  protected xAxisLabelAlignClass(x: number): string {
    if (x <= 0) return 'left-0';
    if (x >= CHART_WIDTH) return '-translate-x-full';
    return '-translate-x-1/2';
  }

  protected toggleWatchForm(): void {
    this.watchFormOpen.update((open) => !open);
    this.watchStatusMessage.set(null);
  }

  protected onWatchSubmit(): void {
    const email = this.watchEmail().trim();
    if (!email) return;

    this.watchSubmitting.set(true);
    this.watchService.watch(this.deal().productId, email).subscribe({
      next: (result) => {
        this.watchStatusMessage.set(result.message);
        this.watchEmail.set('');
        this.watchSubmitting.set(false);
      },
      error: (err) => {
        this.watchStatusMessage.set(friendlyErrorMessage(err));
        this.watchSubmitting.set(false);
      },
    });
  }

  protected vote(helpful: boolean): void {
    if (this.votedHelpful() !== null) return;

    const productId = this.deal().productId;
    this.votedHelpful.set(helpful);
    if (this.isBrowser) {
      localStorage.setItem(`product-vote-${productId}`, helpful ? 'yes' : 'no');
    }
    this.productFeedbackService.vote(productId, helpful).subscribe();
  }

  protected toggleFavorite(): void {
    if (this.favorited()) return;

    // With a token already (an email entered on an earlier product), add
    // directly; the form only opens the first time.
    if (this.favoritesService.getToken()) {
      this.addFavorite();
    } else {
      this.favoriteFormOpen.set(true);
    }
  }

  protected onFavoriteSubmit(): void {
    this.addFavorite(this.favoriteEmail().trim());
  }

  private addFavorite(email?: string): void {
    if (email !== undefined && !email) return;

    this.favoriteSubmitting.set(true);
    this.favoriteErrorMessage.set(null);
    this.favoritesService.add(this.deal().productId, email).subscribe({
      next: (result) => {
        this.favoritesService.saveToken(result.token);
        this.favorited.set(true);
        this.favoriteFormOpen.set(false);
        this.favoriteEmail.set('');
        this.favoriteSubmitting.set(false);
        // recoverySent means this device had no token and an email was sent;
        // otherwise no extra message is needed.
        this.favoriteStatusMessage.set(
          result.recoverySent
            ? 'Added to your watchlist! To see your list on this device, click the link we emailed you (on another browser, click it there too).'
            : null,
        );
      },
      error: (err) => {
        this.favoriteErrorMessage.set(friendlyErrorMessage(err));
        this.favoriteSubmitting.set(false);
      },
    });
  }

  protected selectRange(range: TimeRangeOption): void {
    this.selectedRange.set(range);
  }

  protected close(): void {
    this.closed.emit();
  }

  protected goToStoreUrl(): string {
    const d = this.deal();
    return this.priceHistoryService.goToStoreUrl(d.productId, d.storeUrl);
  }

  /** Counts the store click (see PriceHistoryService). */
  protected trackStoreClick(): void {
    const d = this.deal();
    if (d) this.priceHistoryService.trackStoreClick(d.productId);
  }

  protected onChartMouseMove(event: MouseEvent): void {
    this.updateHoverFromClientX(event.currentTarget as SVGSVGElement, event.clientX);
  }

  protected onChartMouseLeave(): void {
    this.hoverIndex.set(null);
  }

  // mousemove doesn't fire on touch screens (a drag was read as separate
  // taps), so touchstart/touchmove are bound too, with the same nearest-point
  // math, so the point follows a finger across the chart. touchend doesn't
  // clear hoverIndex on purpose: the last value stays visible.
  protected onChartTouchMove(event: TouchEvent): void {
    const touch = event.touches[0];
    if (!touch) return;
    // Stop the page scrolling while the finger is on the chart; otherwise the
    // modal would scroll too.
    event.preventDefault();
    this.updateHoverFromClientX(event.currentTarget as SVGSVGElement, touch.clientX);
  }

  private updateHoverFromClientX(svg: SVGSVGElement, clientX: number): void {
    const idx = nearestPointIndex(svg, clientX, this.coordinates(), CHART_WIDTH);
    if (idx !== null) this.hoverIndex.set(idx);
  }

  private load(productId: number, days: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.priceHistoryService.get(productId, days).subscribe({
      next: (history) => {
        this.points.set(dedupeSameDaySamePrice(history.points));
        this.minPrice.set(history.minPrice);
        this.maxPrice.set(history.maxPrice);
        this.currentPrice.set(history.currentPrice);
        this.loading.set(false);
        this.hasData.set(true);
      },
      error: () => {
        this.error.set("We couldn't load the price history.");
        this.loading.set(false);
      },
    });
  }

  // Five evenly spaced labels. On a short range (a few days of tracking) they
  // could land on the same calendar day and repeat ("Sep 11" twice), so
  // consecutive duplicates are dropped.
  private buildXAxisLabels(points: PricePoint[]): { x: number; label: string }[] {
    if (points.length === 0) return [];

    const times = points.map((p) => new Date(p.scrapedAt).getTime());
    const minTime = Math.min(...times);
    const maxTime = Math.max(...times);

    if (maxTime === minTime) {
      return [{ x: CHART_WIDTH / 2, label: axisDateFormatter.format(new Date(minTime)) }];
    }

    const candidates = Array.from({ length: AXIS_LABEL_COUNT }, (_, i) => {
      const fraction = i / (AXIS_LABEL_COUNT - 1);
      const t = minTime + (maxTime - minTime) * fraction;
      return { x: fraction * CHART_WIDTH, label: axisDateFormatter.format(new Date(t)) };
    });

    return candidates.filter((c, i) => i === 0 || c.label !== candidates[i - 1].label);
  }
}
