import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { BrandComparison } from '../core/brand-comparison.model';
import { BrandComparisonService } from '../core/brand-comparison.service';
import { brandSlug, resolveBrandFromSlug } from '../core/brand-slug';
import { DealsService } from '../core/deals.service';
import { CATEGORY_LABELS } from '../core/category-labels';
import { PageMetaService } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';
import { showNotFound } from '../core/not-found-navigation';

@Component({
  selector: 'app-brand-comparison-page',
  imports: [PricePipe, RouterLink, SiteHeader],
  templateUrl: './brand-comparison-page.html',
})
export class BrandComparisonPage implements OnInit {
  // One place builds the URL: toLowerCase() carried spaces and accented
  // letters into the address and produced a copy that drifted from canonical.
  protected readonly brandSlug = brandSlug;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly comparisonService = inject(BrandComparisonService);
  private readonly dealsService = inject(DealsService);
  private readonly pageMeta = inject(PageMetaService);

  protected readonly comparison = signal<BrandComparison | null>(null);
  protected readonly loading = signal(true);
  protected readonly pairSlug = signal('');

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const pair = params.get('pair') ?? '';
      this.loadComparison(pair);
    });
  }

  private loadComparison(pair: string): void {
    this.loading.set(true);
    const parts = pair.split('-vs-');
    if (parts.length !== 2 || !parts[0] || !parts[1]) {
      showNotFound(this.router);
      return;
    }

    this.pairSlug.set(pair);

    // The URL part is a slug ("optimum-nutrition"); the API expects the real
    // brand name. It's matched against the brand list; if nothing matches the
    // part is sent as is.
    this.dealsService.getFilterOptions().subscribe({
      next: (filters) => {
        const brand1 = resolveBrandFromSlug(parts[0], filters.brands) ?? parts[0];
        const brand2 = resolveBrandFromSlug(parts[1], filters.brands) ?? parts[1];
        this.compareBrands(brand1, brand2);
      },
      error: () => this.compareBrands(parts[0], parts[1]),
    });
  }

  private compareBrands(brand1: string, brand2: string): void {
    this.comparisonService.compare(brand1, brand2).subscribe({
      next: (result) => {
        this.comparison.set(result);
        this.setMeta(result);
        this.loading.set(false);
      },
      // A 404 is kept apart from a TEMPORARY error: telling search engines
      // "this page doesn't exist" because of a momentary backend 5xx would do
      // lasting harm. This page has no error screen yet, so a temporary error
      // falls back to the home page.
      error: (err: HttpErrorResponse) => {
        if (err.status === 404) {
          showNotFound(this.router);
          return;
        }
        void this.router.navigate(['/']);
      },
    });
  }

  private setMeta(comparison: BrandComparison): void {
    const title = `${comparison.brand1} vs ${comparison.brand2} Price Comparison | ${SITE_NAME}`;
    const description = `Compare current average prices of ${comparison.brand1} and ${comparison.brand2} by category, based on real price data.`;

    this.pageMeta.set({
      title,
      description,
      canonicalPath: `/compare/${this.pairSlug()}`,
    });
  }

  protected categoryLabel(category: string): string {
    return CATEGORY_LABELS[category] ?? category;
  }

  protected cheaperBrand(cat: { brand1AvgPrice: number | null; brand2AvgPrice: number | null }): 1 | 2 | null {
    if (cat.brand1AvgPrice === null || cat.brand2AvgPrice === null) return null;
    if (cat.brand1AvgPrice === cat.brand2AvgPrice) return null;
    return cat.brand1AvgPrice < cat.brand2AvgPrice ? 1 : 2;
  }

  // A short summary under the table: in how many categories each brand is
  // cheaper, and where the difference is largest. Derived entirely from the
  // category data already loaded; no extra backend call.
  //
  // "leader" exists on purpose: an earlier template always named brand1 (the
  // alphabetically first brand, winner or not) first, producing confusing
  // sentences that opened with the "cheaper side" and then said 0. The
  // winner is computed separately and used as the single clear subject.
  protected readonly summary = computed(() => {
    const c = this.comparison();
    if (!c || c.categories.length === 0) return null;

    let brand1Wins = 0;
    let brand2Wins = 0;
    let biggestDiff: { category: string; percent: number; cheaper: 1 | 2 } | null = null;

    for (const cat of c.categories) {
      const winner = this.cheaperBrand(cat);
      if (winner === 1) brand1Wins++;
      else if (winner === 2) brand2Wins++;

      if (winner !== null && cat.brand1AvgPrice !== null && cat.brand2AvgPrice !== null) {
        const higher = winner === 1 ? cat.brand2AvgPrice : cat.brand1AvgPrice;
        const lower = winner === 1 ? cat.brand1AvgPrice : cat.brand2AvgPrice;
        const percent = Math.round(((higher - lower) / higher) * 100);
        if (!biggestDiff || percent > biggestDiff.percent) {
          biggestDiff = { category: cat.category, percent, cheaper: winner };
        }
      }
    }

    const ties = c.categories.length - brand1Wins - brand2Wins;
    const leader: 1 | 2 | null = brand1Wins === brand2Wins ? null : brand1Wins > brand2Wins ? 1 : 2;
    const leaderWins = leader === 1 ? brand1Wins : leader === 2 ? brand2Wins : 0;
    const otherWins = leader === 1 ? brand2Wins : leader === 2 ? brand1Wins : 0;

    return { brand1Wins, brand2Wins, ties, leader, leaderWins, otherWins, biggestDiff };
  });
}
