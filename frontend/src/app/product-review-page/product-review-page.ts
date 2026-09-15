import { DOCUMENT, DecimalPipe, isPlatformServer } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, PLATFORM_ID, RESPONSE_INIT, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { dedupeSameDaySamePrice, hoverAlign, nearestPointIndex, tooltipDateLabel } from '../core/chart-hover';
import { buildPageTitle, buildReviewDescription, formatPriceText } from '../core/meta-description';
import { buildProductFacts, buildProductJsonLdDescription, offerAvailability } from '../core/product-facts';
import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { canonicalOrigin } from '../core/canonical-link';
import { CATEGORY_LABELS } from '../core/category-labels';
import { CategoryPriceStats } from '../core/category-price-stats.model';
import { ComparisonService } from '../core/comparison.service';
import { Deal } from '../core/deal.model';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { MARKET, formatWholePrice } from '../core/market';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { PricePoint } from '../core/price-history.model';
import { PriceHistoryService } from '../core/price-history.service';
import { formatRelativeTime } from '../core/relative-time';
import { slugify } from '../core/slugify';
import {
  PROTEIN_REFERENCE_GRAMS,
  pricePerServing,
  proteinRatioPercent,
  proteinReferenceCost,
} from '../core/value-metrics';
import { buildAreaPath, buildLinePath, toCoordinates } from '../core/spark-chart';
import { SiteHeader } from '../site-header/site-header';
import { showNotFound } from '../core/not-found-navigation';

const CHART = { width: 640, height: 160, paddingY: 16 };
const HISTORY_DAYS = 30;
const SIMILAR_PRODUCTS_LIMIT = 4;
const BEST_VALUE_LIMIT = 3;
const CLOSEST_ALTERNATIVES_LIMIT = 2;

interface NutritionRow {
  label: string;
  value: string;
}

// Product review page.
// DELIBERATE LIMIT: we make NO subjective "we tested it, here's how it felt"
// claims; we have no hands-on experience. The analysis rests entirely on our
// own data (price history, nutrition facts, category position). Missing data
// is NOT hidden silently: every field the brand doesn't provide gets an
// honest note, so we don't look one-sided.
@Component({
  selector: 'app-product-review-page',
  imports: [PricePipe, DecimalPipe, RouterLink, SiteHeader],
  templateUrl: './product-review-page.html',
})
export class ProductReviewPage implements OnInit {
  protected readonly displayName = displayName;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dealsService = inject(DealsService);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });
  private readonly isServer = isPlatformServer(inject(PLATFORM_ID));

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly deal = signal<Deal | null>(null);

  // A list built entirely from our own data, in place of the brand's copy;
  // see the note in core/product-facts.ts.
  protected readonly productFacts = computed(() => {
    const deal = this.deal();
    return deal ? buildProductFacts(deal) : [];
  });
  protected readonly points = signal<PricePoint[]>([]);
  protected readonly categoryStats = signal<CategoryPriceStats | null>(null);
  protected readonly similarProducts = signal<Deal[]>([]);
  // "Best value per serving in this category": /api/best-value-per-serving
  // already returns items sorted by price per serving (the same endpoint as
  // the calculator); the current product is removed and the top 3 kept. It
  // answers "X vs Y" comparison intent on this page too, keeping the page
  // genuinely different and indexable.
  protected readonly bestValueInCategory = signal<Deal[]>([]);

  protected readonly chart = CHART;
  protected readonly historyDays = HISTORY_DAYS;

  protected readonly pricePerServing = computed(() => {
    const d = this.deal();
    return d ? pricePerServing(d) : null;
  });

  protected readonly nutritionRows = computed<NutritionRow[]>(() => {
    const json = this.deal()?.nutritionJson;
    if (!json) return [];
    try {
      const parsed = JSON.parse(json) as Record<string, string>;
      return Object.entries(parsed).map(([label, value]) => ({ label, value }));
    } catch {
      return [];
    }
  });

  // The product's position against the category average: a real price
  // difference in percent, not an invented "score".
  protected readonly categoryPricePosition = computed(() => {
    const d = this.deal();
    const stats = this.categoryStats();
    if (!d || !stats || stats.averagePrice <= 0) return null;

    const diffPercent = Math.round(((d.currentPrice - stats.averagePrice) / stats.averagePrice) * 100);
    return { diffPercent, isCheaper: diffPercent < 0 };
  });

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const id = Number(params.get('id'));
      if (!Number.isInteger(id) || id <= 0) {
        showNotFound(this.router);
        return;
      }
      this.load(id);
    });
  }

  private load(id: number): void {
    this.loading.set(true);
    this.loadError.set(false);

    forkJoin({
      deal: this.dealsService.getProductById(id),
      history: this.priceHistoryService.get(id, HISTORY_DAYS).pipe(catchError(() => of({ points: [] as PricePoint[] }))),
    }).subscribe({
      next: ({ deal, history }) => {
        this.deal.set(deal);
        // Drop same-day/same-price repeats, or hover shows the same date over
        // and over (the same bug the modal had).
        this.points.set(dedupeSameDaySamePrice(history.points));
        this.setMeta(deal, this.points().length);
        this.loading.set(false);

        this.bestValueInCategory.set([]);
        if (deal.category) {
          this.dealsService.getCategoryPriceStats(deal.category).subscribe((stats) => this.categoryStats.set(stats));
          this.dealsService
            .getAllProducts({ categories: [deal.category], pageSize: SIMILAR_PRODUCTS_LIMIT + 1 })
            .subscribe((result) => this.similarProducts.set(result.items.filter((d) => d.productId !== id).slice(0, SIMILAR_PRODUCTS_LIMIT)));
          this.dealsService
            .getBestValuePerServing({ category: deal.category, pageSize: BEST_VALUE_LIMIT + 1 })
            .subscribe({
              next: (result) => this.bestValueInCategory.set(result.items.filter((d) => d.productId !== id).slice(0, BEST_VALUE_LIMIT)),
              // With no serving data in the category the endpoint can return
              // 404; the section then stays hidden and the main content is
              // unaffected.
              error: () => this.bestValueInCategory.set([]),
            });
        }
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        // Same split as deals-list.ts: a product that really doesn't exist is
        // a 404, a temporary error a 503. Saying "gone" on a temporary
        // problem would do lasting harm.
        if (err.status === 404) {
          showNotFound(this.router);
          return;
        }
        this.loadError.set(true);
        if (this.responseInit) this.responseInit.status = 503;
      },
    });
  }

  // Price per serving for any Deal (every row of the comparison tables),
  // with the same rules as the backend's CalculateServings.
  protected pricePerServingFor(deal: Deal): number | null {
    return pricePerServing(deal);
  }

  // Cost of a fixed amount of protein, from the shared module
  // (core/value-metrics.ts): the comparison page uses the same function, so
  // the two pages can't show different numbers for the same product.
  protected readonly proteinReferenceGrams = PROTEIN_REFERENCE_GRAMS;

  protected proteinReferenceCostFor(deal: Deal): number | null {
    return proteinReferenceCost(deal);
  }

  // What share of a serving is protein: "how much of what you pay goes to
  // the active ingredient".
  protected proteinRatio(deal: Deal): number | null {
    return proteinRatioPercent(deal);
  }

  // When the per-serving table (bestValueInCategory) is empty (no category,
  // or no product in it with serving data), show at least a PRICE comparison
  // rather than a thin page. similarProducts is fetched by category without
  // requiring serving data; here it is sorted by price and the top 3 kept.
  protected readonly priceFallbackProducts = computed(() =>
    [...this.similarProducts()].sort((a, b) => a.currentPrice - b.currentPrice).slice(0, 3),
  );

  // Products in the same category CLOSEST IN PRICE to this one: a purely
  // numeric closeness, not a subjective "similar product" judgment.
  protected readonly closestAlternatives = computed(() => {
    const current = this.deal();
    if (!current) return [];
    return [...this.similarProducts()]
      .sort((a, b) => Math.abs(a.currentPrice - current.currentPrice) - Math.abs(b.currentPrice - current.currentPrice))
      .slice(0, CLOSEST_ALTERNATIVES_LIMIT);
  });

  // An alternative's difference from this product in one honest sentence:
  // only measurable differences (price, discount, price per serving), never
  // a subjective "better/worse".
  protected comparisonNote(alt: Deal): string {
    const current = this.deal();
    if (!current) return '';

    const parts: string[] = [];
    const priceDiff = alt.currentPrice - current.currentPrice;
    if (Math.abs(priceDiff) >= 1) {
      parts.push(priceDiff < 0 ? `${formatWholePrice(Math.abs(priceDiff))} cheaper` : `${formatWholePrice(priceDiff)} more expensive`);
    }

    const altPerServing = this.pricePerServingFor(alt);
    const currentPerServing = this.pricePerServingFor(current);
    if (altPerServing && currentPerServing && Math.abs(altPerServing - currentPerServing) >= 0.05) {
      parts.push(altPerServing < currentPerServing ? 'better value per serving' : 'more expensive per serving');
    }

    if (alt.discountPercent > 0 && current.discountPercent === 0) {
      parts.push(`${alt.discountPercent}% off right now`);
    }

    return parts.length > 0 ? parts.join(', ') : 'about the same price';
  }

  // /compare-products/{id}-vs-{id}, with the same canonical (ascending) rule
  // as the product comparison page.
  protected comparisonLink(other: Deal): string[] {
    const current = this.deal();
    if (!current) return ['/'];
    return ['/compare-products', ComparisonService.pairSlug(current.productId, other.productId)];
  }

  protected chartPath(): string {
    return buildLinePath(this.coordinates());
  }

  protected chartArea(): string {
    return buildAreaPath(this.coordinates(), CHART.height);
  }

  private coordinates(): [number, number][] {
    const pts = this.points();
    if (pts.length === 0) return [];
    const prices = pts.map((p) => p.price);
    return toCoordinates(pts, Math.min(...prices), Math.max(...prices), CHART);
  }

  // --- Chart interaction ---
  // The math is shared in core/chart-hover.ts: the same logic caused three
  // production bugs in the product modal, and a second copy would bring them back.
  protected readonly hoverIndex = signal<number | null>(null);

  protected readonly hoverInfo = computed(() => {
    const idx = this.hoverIndex();
    if (idx === null) return null;
    const coords = this.coordinates();
    const pts = this.points();
    if (idx >= coords.length || idx >= pts.length) return null;

    const [x, y] = coords[idx];
    return {
      x,
      y,
      align: hoverAlign(x, CHART.width),
      price: pts[idx].price,
      dateLabel: tooltipDateLabel(pts, idx),
    };
  });

  protected onChartMouseMove(event: MouseEvent): void {
    this.updateHover(event.currentTarget as SVGSVGElement, event.clientX);
  }

  protected onChartMouseLeave(): void {
    this.hoverIndex.set(null);
  }

  // mousemove doesn't fire on touch screens; touch events are bound so a
  // finger can follow the chart. touchend doesn't clear it on purpose: the
  // last value stays visible.
  protected onChartTouchMove(event: TouchEvent): void {
    const touch = event.touches[0];
    if (!touch) return;
    // Stop the page scrolling while on the chart.
    event.preventDefault();
    this.updateHover(event.currentTarget as SVGSVGElement, touch.clientX);
  }

  private updateHover(svg: SVGSVGElement, clientX: number): void {
    this.hoverIndex.set(nearestPointIndex(svg, clientX, this.coordinates(), CHART.width));
  }

  protected categoryLabel(slug: string | null): string {
    if (!slug) return '—';
    return CATEGORY_LABELS[slug] ?? slug;
  }

  protected lastChecked(): string {
    const d = this.deal();
    return d ? formatRelativeTime(d.scrapedAt) : '';
  }

  protected storeUrl(): string {
    const d = this.deal();
    return d ? this.priceHistoryService.goToStoreUrl(d.productId, d.storeUrl) : '#';
  }

  /** Counts the store click (see PriceHistoryService). */
  protected trackStoreClick(): void {
    const d = this.deal();
    if (d) this.priceHistoryService.trackStoreClick(d.productId);
  }

  protected productLink(d: Deal): string[] {
    return ['/product', String(d.productId), slugify(d.productName)];
  }

  private setMeta(deal: Deal, historyDays: number): void {
    const slug = slugify(deal.productName);
    const name = displayName(deal.productName);
    // No year in the title on purpose: a hard-coded year makes every title
    // look stale the following year, and Google reads dates elsewhere.
    const title = buildPageTitle(name, 'Review', deal.brandName);
    // A PRODUCT-SPECIFIC description: a searcher types the product name and
    // wants the price. The day count and discount claim only appear when
    // the data supports them (see core/meta-description.ts).
    const description = buildReviewDescription({
      displayName: name,
      priceText: formatPriceText(deal.currentPrice),
      discountPercent: deal.discountPercent,
      historyDays,
    });

    this.pageMeta.set({
      title,
      description,
      canonicalPath: `/review/${deal.productId}/${slug}`,
      ogType: 'article',
      ogImage: deal.imageUrl ?? undefined,
    });

    const origin = canonicalOrigin(this.document);
    const jsonLd = {
      '@context': 'https://schema.org',
      '@type': 'Product',
      name,
      sku: String(deal.productId),
      ...(deal.imageUrl ? { image: deal.imageUrl } : {}),
      brand: { '@type': 'Brand', name: deal.brandName },
      description: buildProductJsonLdDescription(deal),
      offers: {
        '@type': 'Offer',
        url: `${origin}/product/${deal.productId}/${slug}`,
        priceCurrency: MARKET.currency,
        price: deal.currentPrice.toFixed(2),
        ...offerAvailability(deal.inStock),
      },
      ...(deal.ratingValue !== null && deal.ratingCount !== null
        ? {
            aggregateRating: {
              '@type': 'AggregateRating',
              ratingValue: deal.ratingValue,
              reviewCount: deal.ratingCount,
              bestRating: 5,
            },
          }
        : {}),
      ...(this.nutritionRows().length > 0
        ? {
            additionalProperty: this.nutritionRows().map((r) => ({
              '@type': 'PropertyValue',
              name: r.label,
              value: r.value,
            })),
          }
        : {}),
    };
    upsertJsonLdScript(this.document, null, jsonLd);

    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        ...(deal.category ? [{ name: this.categoryLabel(deal.category), path: `/category/${deal.category}` }] : []),
        { name: `${name} Review`, path: `/review/${deal.productId}/${slug}` },
      ]),
    );
  }
}
