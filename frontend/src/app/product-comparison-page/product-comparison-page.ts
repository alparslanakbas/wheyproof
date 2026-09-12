import { DOCUMENT, DecimalPipe, Location, isPlatformServer } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, PLATFORM_ID, RESPONSE_INIT, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { CATEGORY_LABELS } from '../core/category-labels';
import { ComparisonService } from '../core/comparison.service';
import { Deal } from '../core/deal.model';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import {
  PROTEIN_REFERENCE_GRAMS,
  pricePerServing,
  proteinRatioPercent,
  proteinReferenceCost,
  servingsInPackage,
} from '../core/value-metrics';
import { PageMetaService } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { PricePoint } from '../core/price-history.model';
import { PriceHistoryService } from '../core/price-history.service';
import { formatRelativeTime } from '../core/relative-time';
import { SITE_NAME } from '../core/site-identity';
import { slugify } from '../core/slugify';
import { buildAreaPath, buildLinePath, toCoordinates } from '../core/spark-chart';
import { SiteHeader } from '../site-header/site-header';
import { showNotFound } from '../core/not-found-navigation';

// Comparison chart: the same dimensions for both products so they read side by side.
const CHART = { width: 320, height: 100, paddingY: 10 };
const HISTORY_DAYS = 30;

interface ComparedProduct {
  deal: Deal;
  points: PricePoint[];
  servings: number | null;
  pricePerServing: number | null;
  // Share of a serving that is protein, and the cost of a fixed amount of
  // protein: measures free of package size that compare two products
  // directly (see core/value-metrics.ts).
  proteinRatio: number | null;
  proteinCost: number | null;
}

// Two products side by side. ALL data is fetched fresh: the comparison bar
// only carries which products were picked; fields like price aren't read
// from it (they may be stale).
//
// SEO NOTE: these pages are NOT in the sitemap. A few thousand products make
// millions of pairs; offering all of them to crawlers would only grow the
// "discovered, not indexed" pile. Shared links still work and render on the
// server.
@Component({
  selector: 'app-product-comparison-page',
  imports: [PricePipe, DecimalPipe, RouterLink, SiteHeader],
  templateUrl: './product-comparison-page.html',
})
export class ProductComparisonPage implements OnInit {
  protected readonly displayName = displayName;
  private readonly location = inject(Location);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dealsService = inject(DealsService);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });
  private readonly isServer = isPlatformServer(inject(PLATFORM_ID));
  protected readonly comparison = inject(ComparisonService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly products = signal<ComparedProduct[]>([]);

  protected readonly chart = CHART;
  protected readonly historyDays = HISTORY_DAYS;

  // Which of the two is better on each measure; a tie highlights neither.
  protected readonly cheaperIndex = computed(() => this.betterIndex((p) => p.deal.currentPrice, 'min'));
  protected readonly cheaperPerServingIndex = computed(() =>
    this.betterIndex((p) => p.pricePerServing, 'min'),
  );
  protected readonly biggerPackageIndex = computed(() => this.betterIndex((p) => p.servings, 'max'));
  // A HIGHER protein ratio and a LOWER fixed-protein cost are better.
  protected readonly denserProteinIndex = computed(() => this.betterIndex((p) => p.proteinRatio, 'max'));
  protected readonly cheaperProteinIndex = computed(() => this.betterIndex((p) => p.proteinCost, 'min'));
  protected readonly proteinReferenceGrams = PROTEIN_REFERENCE_GRAMS;

  // Merges both products' nutrition tables (when present) into one row list:
  // product A's own order is kept, rows only B has are appended. A row a
  // brand doesn't provide shows "—"; nothing is invented.
  protected readonly nutritionRows = computed(() => {
    const list = this.products();
    if (list.length < 2) return [];

    const tableA = this.parseNutrition(list[0].deal.nutritionJson);
    const tableB = this.parseNutrition(list[1].deal.nutritionJson);
    if (!tableA && !tableB) return [];

    const labels = [...Object.keys(tableA ?? {}), ...Object.keys(tableB ?? {})];
    const seen = new Set<string>();
    return labels
      .filter((label) => (seen.has(label) ? false : (seen.add(label), true)))
      .map((label) => ({ label, values: [tableA?.[label] ?? null, tableB?.[label] ?? null] as [string | null, string | null] }));
  });

  private parseNutrition(json: string | null): Record<string, string> | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as Record<string, string>;
    } catch {
      return null;
    }
  }

  private betterIndex(pick: (p: ComparedProduct) => number | null, mode: 'min' | 'max'): number | null {
    const list = this.products();
    if (list.length < 2) return null;

    const a = pick(list[0]);
    const b = pick(list[1]);
    if (a == null || b == null || a === b) return null;

    const firstWins = mode === 'min' ? a < b : a > b;
    return firstWins ? 0 : 1;
  }

  protected chartPath(points: PricePoint[]): string {
    return buildLinePath(this.coordinates(points));
  }

  protected chartArea(points: PricePoint[]): string {
    return buildAreaPath(this.coordinates(points), CHART.height);
  }

  private coordinates(points: PricePoint[]) {
    if (points.length === 0) return [];
    const prices = points.map((p) => p.price);
    return toCoordinates(points, Math.min(...prices), Math.max(...prices), CHART);
  }

  protected productLink(deal: Deal): string[] {
    return ['/product', String(deal.productId), slugify(deal.productName)];
  }

  protected categoryLabel(slug: string | null): string {
    if (!slug) return '—';
    return CATEGORY_LABELS[slug] ?? slug;
  }

  protected lastChecked(deal: Deal): string {
    return formatRelativeTime(deal.scrapedAt);
  }

  protected storeUrl(deal: Deal): string {
    return this.priceHistoryService.goToStoreUrl(deal.productId, deal.storeUrl);
  }

  /** Counts the store click; the link goes straight to the store, so /go/{id}
   *  can no longer count it (see PriceHistoryService). */
  protected trackStoreClick(productId: number): void {
    this.priceHistoryService.trackStoreClick(productId);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const ids = this.parsePair(params.get('pair'));
      if (!ids) {
        showNotFound(this.router);
        return;
      }

      this.load(ids[0], ids[1]);
    });
  }

  // "29-vs-603" → [29, 603]. An invalid shape returns null and shows the
  // 404 page.
  private parsePair(pair: string | null): [number, number] | null {
    if (!pair) return null;

    const parts = pair.split('-vs-');
    if (parts.length !== 2) return null;

    const a = Number(parts[0]);
    const b = Number(parts[1]);
    if (!Number.isInteger(a) || !Number.isInteger(b) || a <= 0 || b <= 0 || a === b) return null;

    return [a, b];
  }

  private load(idA: number, idB: number): void {
    this.loading.set(true);
    this.loadError.set(false);

    forkJoin({
      dealA: this.dealsService.getProductById(idA),
      dealB: this.dealsService.getProductById(idB),
      historyA: this.priceHistoryService.get(idA, HISTORY_DAYS).pipe(catchError(() => of({ points: [] as PricePoint[] }))),
      historyB: this.priceHistoryService.get(idB, HISTORY_DAYS).pipe(catchError(() => of({ points: [] as PricePoint[] }))),
    }).subscribe({
      next: ({ dealA, dealB, historyA, historyB }) => {
        this.products.set([
          this.build(dealA, historyA.points),
          this.build(dealB, historyB.points),
        ]);
        this.setMeta(dealA, dealB);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);

        // If one of the products really doesn't exist (404), this pair will
        // never be valid. A temporary error (network, backend 5xx) gets a 503
        // "try again later" instead: no "gone" signal for a passing problem.
        // Same split as deals-list.ts.
        if (err.status === 404) {
          showNotFound(this.router);
          return;
        }

        this.loadError.set(true);
        if (this.responseInit) this.responseInit.status = 503;
      },
    });
  }

  // Servings and price per serving come from the shared module, so the
  // review page and this page can't disagree on the same product.
  private build(deal: Deal, points: PricePoint[]): ComparedProduct {
    return {
      deal,
      points,
      servings: servingsInPackage(deal),
      pricePerServing: pricePerServing(deal),
      proteinRatio: proteinRatioPercent(deal),
      proteinCost: proteinReferenceCost(deal),
    };
  }

  /**
   * The store button's first line: the part that tells the products apart.
   *
   * With only "Go to {brand}" on it, comparing two products from the same
   * brand showed two IDENTICAL buttons side by side, and it was unclear which
   * went where. The action stays on the second line; this text sits above
   * it, small and muted. Size/flavor is preferred (short, and exactly what
   * separates the two); without either it falls back to the product name.
   */
  protected storeButtonLabel(deal: Deal): string {
    const distinguishing = [deal.size, deal.flavor].filter(Boolean).join(' · ');
    return distinguishing || displayName(deal.productName);
  }

  /**
   * "Clear comparison".
   *
   * Calling only `comparison.clear()` did nothing visible: this page loads
   * its products from the ROUTE, not the service, so the screen stayed the
   * same. Once the selection is cleared the page itself is pointless, so it
   * goes back where the visitor came from, or to the home page when the link
   * was opened directly and there's nowhere to go back to.
   */
  protected clearComparison(): void {
    this.comparison.clear();

    if (this.location.getState() && window.history.length > 1) {
      this.location.back();
      return;
    }

    this.router.navigate(['/']);
  }

  private setMeta(a: Deal, b: Deal): void {
    const nameA = displayName(a.productName);
    const nameB = displayName(b.productName);
    const title = `${nameA} vs ${nameB}: Price Comparison | ${SITE_NAME}`;
    this.pageMeta.set({
      title,
      description: `Compare ${a.brandName} ${nameA} and ${b.brandName} ${nameB} side by side: current price, cost per serving and 30-day price history.`,
      canonicalPath: `/compare-products/${ComparisonService.pairSlug(a.productId, b.productId)}`,
    });
  }
}
