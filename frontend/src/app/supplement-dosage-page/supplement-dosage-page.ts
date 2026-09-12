import { DOCUMENT, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ComparisonService } from '../core/comparison.service';
import { Deal } from '../core/deal.model';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { MARKET } from '../core/market';
import { showNotFound } from '../core/not-found-navigation';
import { packageGrams } from '../core/package-size';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { slugify } from '../core/slugify';
import { SupplementDosage, findSupplementDosage } from '../core/supplement-dosages';
import { SiteHeader } from '../site-header/site-header';

interface DosageProduct {
  deal: Deal;
  totalGrams: number;
  daysSupply: number;
  costPerDay: number;
}

interface ExamplePair {
  a: Deal;
  b: Deal;
  slug: string;
}

// The same pattern as the home page and protein calculator; the table is a
// row list, so 12 is a sensible page size.
const PAGE_SIZE = 12;
const SEARCH_DEBOUNCE_MS = 350;

// ONE component, driven by configuration (supplement-dosages.ts), produces
// the creatine/beta-alanine/citrulline/betaine/EAA pages. A component per
// supplement would repeat hundreds of lines.
//
// KEY DESIGN DECISION: these supplements' doses do NOT scale with body
// weight; fixed ranges are used in the literature and in practice. An
// "enter your weight, we'll calculate your dose" tool would be invented. An
// honest range is shown instead, and the real calculation uses our data
// advantage: how many days a package lasts and what it costs per day.
@Component({
  selector: 'app-supplement-dosage-page',
  imports: [PricePipe, DecimalPipe, FormsModule, RouterLink, SiteHeader],
  templateUrl: './supplement-dosage-page.html',
})
export class SupplementDosagePage implements OnInit {
  protected readonly displayName = displayName;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dealsService = inject(DealsService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private structuredDataEl: HTMLScriptElement | null = null;

  protected readonly config = signal<SupplementDosage | null>(null);
  protected readonly dailyGrams = signal<number>(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  private readonly products = signal<Deal[]>([]);

  // The whole category arrives in one request (≤100 products), so search,
  // brand filter and pagination run CLIENT-SIDE with no extra network call;
  // changing the daily dose re-sorts instantly.
  protected readonly searchQuery = signal('');
  protected readonly selectedBrands = signal<Set<string>>(new Set());
  protected readonly currentPage = signal(1);
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

  protected readonly availableBrands = computed(() =>
    [...new Set(this.products().map((p) => p.brandName))].sort((a, b) => a.localeCompare(b, MARKET.locale)),
  );

  protected readonly hasActiveFilters = computed(
    () => this.searchQuery().trim().length > 0 || this.selectedBrands().size > 0,
  );

  // Example comparison pairs from the same category, picked at random ONCE
  // when the page loads and stable across renders after that.
  protected readonly examplePairs = signal<ExamplePair[]>([]);

  // For the chosen daily dose: how many days each product lasts and what it
  // costs per day. Only products with a KNOWN package weight are listed; no
  // guessing for the rest. Search and brand filters apply BEFORE sorting.
  protected readonly dosageProducts = computed<DosageProduct[]>(() => {
    const grams = this.dailyGrams();
    if (!grams || grams <= 0) return [];

    const query = this.searchQuery().trim().toLowerCase();
    const brands = this.selectedBrands();

    return this.products()
      .filter((deal) => !query || deal.productName.toLowerCase().includes(query))
      .filter((deal) => brands.size === 0 || brands.has(deal.brandName))
      .map((deal) => {
        const totalGrams = this.totalPackageGrams(deal);
        if (!totalGrams) return null;

        const daysSupply = totalGrams / grams;
        if (daysSupply < 1) return null;

        return { deal, totalGrams, daysSupply, costPerDay: deal.currentPrice / daysSupply };
      })
      .filter((x): x is DosageProduct => x !== null)
      .sort((a, b) => a.costPerDay - b.costPerDay);
  });

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.dosageProducts().length / PAGE_SIZE)));

  protected readonly pagedDosageProducts = computed(() => {
    const all = this.dosageProducts();
    const page = Math.min(this.currentPage(), this.totalPages());
    const start = (page - 1) * PAGE_SIZE;
    return all.slice(start, start + PAGE_SIZE);
  });

  protected onDailyGramsChange(value: number): void {
    this.dailyGrams.set(value);
    this.currentPage.set(1);
  }

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
    if (this.searchDebounceHandle) clearTimeout(this.searchDebounceHandle);
    this.searchDebounceHandle = setTimeout(() => this.currentPage.set(1), SEARCH_DEBOUNCE_MS);
  }

  protected toggleBrand(brand: string): void {
    const current = new Set(this.selectedBrands());
    current.has(brand) ? current.delete(brand) : current.add(brand);
    this.selectedBrands.set(current);
    this.currentPage.set(1);
  }

  protected clearFilters(): void {
    this.searchQuery.set('');
    this.selectedBrands.set(new Set());
    this.currentPage.set(1);
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) return;
    this.currentPage.set(page);
  }

  protected productLink(deal: Deal): string[] {
    return ['/product', String(deal.productId), slugify(deal.productName)];
  }

  // Total grams in the package, from two sources:
  // (1) the size field ("300 g", "2.2 lb", "10.6 oz");
  // (2) servings per package × serving size, when the size is missing. Both
  //     come from the brand's own data, so the product is derived, not guessed.
  // Capsule counts ("120 Capsules") have no weight and stay out.
  private totalPackageGrams(deal: Deal): number | null {
    const fromSize = packageGrams(deal.size);
    if (fromSize) return fromSize;

    if (deal.servingsPerPackage && deal.servingsPerPackage > 0 && deal.servingSizeGrams && deal.servingSizeGrams > 0) {
      return deal.servingsPerPackage * deal.servingSizeGrams;
    }

    return null;
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const slug = params.get('slug') ?? '';
      const config = findSupplementDosage(slug);

      if (!config) {
        showNotFound(this.router);
        return;
      }

      this.config.set(config);
      this.dailyGrams.set(config.defaultDailyGrams);
      // The component instance is reused from one dosage page to another
      // (creatine → beta-alanine); the previous page's filters and page
      // must not leak into the new one.
      this.searchQuery.set('');
      this.selectedBrands.set(new Set());
      this.currentPage.set(1);
      this.setMeta(config);
      this.loadProducts(config);
    });
  }

  private setMeta(config: SupplementDosage): void {
    this.pageMeta.set({
      title: config.title,
      description: config.description,
      canonicalPath: `/calculators/${config.slug}`,
    });

    this.structuredDataEl = upsertJsonLdScript(this.document, this.structuredDataEl, {
      '@context': 'https://schema.org',
      '@type': 'WebApplication',
      name: config.h1,
      applicationCategory: 'HealthApplication',
      operatingSystem: 'Web',
      offers: { '@type': 'Offer', price: '0', priceCurrency: MARKET.currency },
    });
  }

  private loadProducts(config: SupplementDosage): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.dealsService
      // expandSynonyms: false. With synonyms, an "alanine" search pulled in
      // the whole amino acid category (arginine products showed up on the
      // beta-alanine page). Here we want exactly that one ingredient.
      .getAllProducts({
        categories: config.category ? [config.category] : [],
        search: config.searchTerm ?? undefined,
        pageSize: 100,
        expandSynonyms: false,
      })
      .subscribe({
        next: (result) => {
          this.products.set(result.items);
          this.examplePairs.set(this.pickExamplePairs(result.items));
          this.loading.set(false);
        },
        error: () => {
          this.loadError.set(true);
          this.loading.set(false);
        },
      });
  }

  // Three random, distinct pairs; all from the same category, so they're
  // relevant by construction. A simple shuffle is enough for a few pairs.
  private pickExamplePairs(list: Deal[]): ExamplePair[] {
    if (list.length < 2) return [];

    const shuffled = [...list].sort(() => Math.random() - 0.5);
    const pairs: ExamplePair[] = [];
    for (let i = 0; i + 1 < shuffled.length && pairs.length < 3; i += 2) {
      const [a, b] = [shuffled[i], shuffled[i + 1]];
      pairs.push({ a, b, slug: ComparisonService.pairSlug(a.productId, b.productId) });
    }
    return pairs;
  }
}
