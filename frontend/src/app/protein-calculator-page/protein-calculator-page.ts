import { DOCUMENT, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { Deal } from '../core/deal.model';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { MARKET } from '../core/market';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { PricePipe } from '../core/price.pipe';
import { SITE_NAME } from '../core/site-identity';
import { slugify } from '../core/slugify';
import { pricePerServing, servingsInPackage } from '../core/value-metrics';
import { SiteHeader } from '../site-header/site-header';

const KG_PER_LB = 0.45359237;

type WeightUnit = 'lb' | 'kg';

interface ActivityLevel {
  id: string;
  label: string;
  description: string;
  // Grams of protein per kg of body weight: ranges widely used in sports
  // nutrition sources, not invented. A RANGE rather than one number, because
  // in reality there's no single right number.
  minPerKg: number;
  maxPerKg: number;
}

const ACTIVITY_LEVELS: ActivityLevel[] = [
  {
    id: 'sedentary',
    label: 'Sedentary',
    description: "I don't exercise regularly",
    minPerKg: 0.8,
    maxPerKg: 1.0,
  },
  {
    id: 'active',
    label: 'Regular exercise',
    description: 'Training 3-5 days a week',
    minPerKg: 1.2,
    maxPerKg: 1.6,
  },
  {
    id: 'intense',
    label: 'Intense training',
    description: 'Focused on building muscle, 5+ days a week',
    minPerKg: 1.6,
    maxPerKg: 2.2,
  },
];

interface ProductValue {
  deal: Deal;
  pricePerServing: number;
  servings: number;
}

// The table uses a smaller page size than the product grids (24): it's a
// row list, and it keeps the SSR output light.
const PAGE_SIZE = 12;
const SEARCH_DEBOUNCE_MS = 350;

// The idea of this page: "how much protein do I need" is a high-volume
// search and most competitors have a calculator, but none has LIVE prices.
// We have both, so the result links straight to a "best value per serving"
// list. That list only includes products whose serving size is ACTUALLY
// known; no "30 g = 1 serving" assumption is ever made.
@Component({
  selector: 'app-protein-calculator-page',
  imports: [PricePipe, DecimalPipe, FormsModule, RouterLink, SiteHeader],
  templateUrl: './protein-calculator-page.html',
})
export class ProteinCalculatorPage implements OnInit {
  protected readonly displayName = displayName;
  private readonly dealsService = inject(DealsService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private structuredDataEl: HTMLScriptElement | null = null;

  protected readonly activityLevels = ACTIVITY_LEVELS;

  // The market's unit is the default (lb on the US site, kg in the UK
  // section); the other stays one tap away. The ranges are defined per kg.
  protected readonly weightUnit = signal<WeightUnit>(MARKET.measurement === 'metric' ? 'kg' : 'lb');
  protected readonly weight = signal<number | null>(null);
  protected readonly activityId = signal<string>('active');
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  private readonly products = signal<Deal[]>([]);

  // Table filters, the same pattern as the home page (debounced search,
  // brand chips, pagination).
  protected readonly searchQuery = signal('');
  protected readonly selectedBrands = signal<Set<string>>(new Set());
  protected readonly availableBrands = signal<string[]>([]);
  protected readonly currentPage = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

  protected readonly hasActiveFilters = computed(
    () => this.searchQuery().trim().length > 0 || this.selectedBrands().size > 0,
  );

  protected readonly selectedActivity = computed(
    () => ACTIVITY_LEVELS.find((level) => level.id === this.activityId()) ?? ACTIVITY_LEVELS[1],
  );

  // The same range expressed per pound, for the explanation under the result.
  protected readonly perLbRange = computed(() => {
    const level = this.selectedActivity();
    return {
      min: Math.round(level.minPerKg * KG_PER_LB * 100) / 100,
      max: Math.round(level.maxPerKg * KG_PER_LB * 100) / 100,
    };
  });

  // No result until a weight is entered: filling in a default weight and
  // saying "this is what you need" would mislead.
  protected readonly dailyProtein = computed(() => {
    const value = this.weight();
    if (!value || value <= 0) return null;

    const kg = this.weightUnit() === 'lb' ? value * KG_PER_LB : value;
    if (kg > 400) return null;

    const level = this.selectedActivity();
    return {
      min: Math.round(kg * level.minPerKg),
      max: Math.round(kg * level.maxPerKg),
    };
  });

  // The backend already returns a filtered list sorted by price per serving
  // (see GetBestValuePerServingAsync); servings and unit price are computed
  // here only for display, with the shared rules in core/value-metrics.ts.
  protected readonly bestValueProducts = computed<ProductValue[]>(() =>
    this.products()
      .map((deal) => {
        const servings = servingsInPackage(deal);
        const perServing = pricePerServing(deal);
        if (!servings || perServing === null) return null;
        return { deal, pricePerServing: perServing, servings };
      })
      .filter((item): item is ProductValue => item !== null),
  );

  protected setWeightUnit(unit: WeightUnit): void {
    if (unit === this.weightUnit()) return;
    // Convert what was typed so switching units doesn't change the result.
    const value = this.weight();
    if (value && value > 0) {
      const converted = unit === 'kg' ? value * KG_PER_LB : value / KG_PER_LB;
      this.weight.set(Math.round(converted * 10) / 10);
    }
    this.weightUnit.set(unit);
  }

  protected productLink(deal: Deal): string[] {
    return ['/product', String(deal.productId), slugify(deal.productName)];
  }

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
    if (this.searchDebounceHandle) clearTimeout(this.searchDebounceHandle);
    this.searchDebounceHandle = setTimeout(() => {
      this.currentPage.set(1);
      this.loadProducts();
    }, SEARCH_DEBOUNCE_MS);
  }

  protected toggleBrand(brand: string): void {
    const current = new Set(this.selectedBrands());
    current.has(brand) ? current.delete(brand) : current.add(brand);
    this.selectedBrands.set(current);
    this.currentPage.set(1);
    this.loadProducts();
  }

  protected clearFilters(): void {
    this.searchQuery.set('');
    this.selectedBrands.set(new Set());
    this.currentPage.set(1);
    this.loadProducts();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) return;
    this.currentPage.set(page);
    this.loadProducts();
  }

  private loadProducts(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.dealsService
      .getBestValuePerServing({
        category: 'protein-powder',
        brands: [...this.selectedBrands()],
        search: this.searchQuery().trim() || undefined,
        page: this.currentPage(),
        pageSize: PAGE_SIZE,
      })
      .subscribe({
        next: (result) => {
          this.products.set(result.items);
          this.totalCount.set(result.totalCount);
          this.totalPages.set(result.totalPages);
          this.loading.set(false);
        },
        error: () => {
          this.loadError.set(true);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Daily Protein Calculator | ${SITE_NAME}`,
      description:
        'Work out your daily protein needs from body weight and training load, then compare against current prices to see the best value protein powders per serving.',
      canonicalPath: '/calculators/protein',
    });

    // Structured data that helps Google understand calculator pages.
    this.structuredDataEl = upsertJsonLdScript(this.document, this.structuredDataEl, {
      '@context': 'https://schema.org',
      '@type': 'WebApplication',
      name: 'Daily Protein Calculator',
      applicationCategory: 'HealthApplication',
      operatingSystem: 'Web',
      offers: { '@type': 'Offer', price: '0', priceCurrency: MARKET.currency },
    });

    // Brand chips: only brands whose price per serving can actually be
    // calculated in this category (no chip that returns nothing).
    this.dealsService.getBestValueBrands('protein-powder').subscribe({
      next: (brands) => this.availableBrands.set(brands),
      error: () => this.availableBrands.set([]),
    });

    this.loadProducts();
  }
}
