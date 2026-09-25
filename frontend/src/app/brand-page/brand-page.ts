import { DOCUMENT, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { LatestRequest } from '../core/latest-request';
import { buildBrandCategoryFaqs, buildBrandFaqs } from '../core/brand-faqs';
import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { BrandStats } from '../core/brand-stats.model';
import { CategoryPriceStats } from '../core/category-price-stats.model';
import { brandSlug, resolveBrandFromSlug } from '../core/brand-slug';
import { CATEGORY_LABELS } from '../core/category-labels';
import { ComparisonService } from '../core/comparison.service';
import { Coupon } from '../core/coupon.model';
import { CouponsService } from '../core/coupons.service';
import { Deal } from '../core/deal.model';
import { productHref, shouldHandleInApp } from '../core/product-link';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { pageFromQuery, paginatedTitle, pageWindow } from '../core/pagination-window';
import { PricePipe } from '../core/price.pipe';
import { PricePoint } from '../core/price-history.model';
import { PriceHistoryService } from '../core/price-history.service';
import { showNotFound } from '../core/not-found-navigation';
import { formatRelativeTime } from '../core/relative-time';
import { SITE_NAME } from '../core/site-identity';
import { pricePerServing } from '../core/value-metrics';
import { ProductCardSparkline } from '../product-card-sparkline/product-card-sparkline';
import { ProductModal } from '../product-modal/product-modal';
import { SiteHeader } from '../site-header/site-header';

type ViewMode = 'deals' | 'store' | 'all';
const PAGE_SIZE = 24;
// The retailer view lists ONLY retailer listings, apart from the brand's own
// storefront. The value MUST match the backend's
// `DealsQueryService.DealerSellerLabel` exactly: the filter matches on it.
const RETAILER_LABEL = 'Retailers';
// Samples shown in the block. The point is a door into the retailer view,
// not the full list; the rest is reached through its pagination.
const RETAILER_SAMPLE_COUNT = 6;
const SEARCH_DEBOUNCE_MS = 350;

@Component({
  selector: 'app-brand-page',
  imports: [PricePipe, DecimalPipe, FormsModule, RouterLink, ProductCardSparkline, ProductModal, SiteHeader],
  templateUrl: './brand-page.html',
})
export class BrandPage implements OnInit {
  // For brand links in the template: toLowerCase() carried "Transparent Labs"
  // into the address with a space, while the canonical used brandSlug.
  protected readonly brandSlug = brandSlug;

  protected readonly displayName = displayName;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dealsService = inject(DealsService);
  private readonly couponsService = inject(CouponsService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private readonly priceHistoryService = inject(PriceHistoryService);
  protected readonly comparison = inject(ComparisonService);
  private breadcrumbEl: HTMLScriptElement | null = null;
  private faqEl: HTMLScriptElement | null = null;

  protected readonly categoryPriceStats = signal<CategoryPriceStats | null>(null);

  // FAQ for the brand x category intersection. Separate from the brand
  // page's: those focus on coupons, these on the brand's price position in
  // the category.
  protected readonly brandCategoryFaqs = computed(() => {
    const category = this.fixedCategory();
    const brand = this.brandName();
    if (!category || !brand) return [];
    const stats = this.brandStats();
    return buildBrandCategoryFaqs({
      brandName: brand,
      categoryLabel: this.fixedCategoryLabel(),
      productCount: stats?.totalProducts ?? null,
      averagePrice: stats?.averagePrice ?? null,
      categoryAveragePrice: this.categoryPriceStats()?.averagePrice ?? null,
      averageDiscountPercent: stats?.averageDiscountPercent ?? null,
    });
  });

  // Brand FAQ, only on the brand's main page (not the intersection), where
  // "coupon code" searches land. Computed because it depends on the coupons
  // and the brand stats.
  protected readonly brandFaqs = computed(() => {
    if (this.fixedCategory()) return [];
    const brand = this.brandName();
    if (!brand) return [];
    const stats = this.brandStats();
    return buildBrandFaqs({
      brandName: brand,
      // The FAQ says "use this code"; sales WITHOUT a code (applied
      // automatically) must not appear there, or people look for a code
      // that doesn't exist at checkout.
      couponCodes: this.coupons()
        .map((c) => c.code)
        .filter((code): code is string => code !== null),
      totalProducts: stats?.totalProducts ?? null,
      averageDiscountPercent: stats?.averageDiscountPercent ?? null,
      topCategoryLabel: this.topCategoryLabel(),
    });
  });

  protected readonly brandName = signal<string>('');
  protected readonly coupons = signal<Coupon[]>([]);
  protected readonly otherBrands = signal<string[]>([]);

  // Brand x category page (/brand/:brandSlug/:categorySlug). When set, the
  // category is FIXED: chips hide and title/meta/canonical are specific to
  // it. When empty, the page is the brand page (all categories, filterable
  // with chips): one component, two modes.
  protected readonly fixedCategory = signal<string | null>(null);
  protected readonly fixedCategoryLabel = signal<string>('');
  // Categories where this brand really has products, for the internal links
  // at the bottom (no links to empty combinations).
  protected readonly brandCategories = signal<{ slug: string; label: string; count: number }[]>([]);
  // Original statistics from our own data.
  protected readonly brandStats = signal<BrandStats | null>(null);
  protected readonly topCategoryLabel = computed(() => {
    const cats = this.brandCategories();
    if (cats.length === 0) return null;
    return [...cats].sort((a, b) => b.count - a.count)[0].label;
  });
  protected readonly loading = signal(true);
  // "loadError", not "notFound": only set when the /api/filters request
  // FAILS (network/API error). An invalid brand slug goes to the not-found
  // page and never lands here.
  protected readonly loadError = signal(false);
  protected readonly itemsError = signal(false);

  // All products, not only discounted ones (same fix as the category page).
  protected readonly viewMode = signal<ViewMode>('all');
  protected readonly items = signal<Deal[]>([]);
  // One batched request for the cards' mini sparklines (see deals-list.ts).
  protected readonly sparklines = signal<Map<number, PricePoint[]>>(new Map());
  // Only the newest request is handled: a stale response arriving late
  // after a quick filter change must not overwrite the list.
  private readonly listRequest = new LatestRequest();
  private readonly sparklineRequest = new LatestRequest();
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  protected readonly currentPage = signal(1);
  // Page numbers for the pagination bar (null = "…").
  protected readonly pageItems = computed(() => pageWindow(this.currentPage(), this.totalPages()));

  // --- Retailer listings -------------------------------------------------
  //
  // WHY: the brand page is the brand's OWN storefront, and for brands with
  // their own store, retailer copies are left out of the list. On the Turkish
  // site that was measured: 59% of the catalog was retailer listings, and
  // most got NO LINK FROM ANY PAGE; Google knew them only from the sitemap
  // and left them "Discovered - not indexed" (a sitemap gives discovery, not
  // priority).
  //
  // The storefront stays intact: retailer listings get a SEPARATE view
  // (`?seller=retailers`), the main list shows only a block linking to it, and
  // that view is paginated, so every retailer listing becomes crawlable.
  protected readonly retailerView = signal(false);
  protected readonly retailerSamples = signal<Deal[]>([]);
  protected readonly retailerTotal = signal(0);
  // Does the brand sell on its own site? If unknown, the block is NOT shown:
  // for brands without their own store, retailer listings are already in the
  // main list and the block would repeat them.
  private readonly brandHasOwnStore = signal(false);
  protected readonly showRetailerBlock = computed(
    () => !this.retailerView() && this.brandHasOwnStore() && this.retailerTotal() > 0,
  );
  protected readonly sortBy = signal<string>('');

  // Search and filters. The brand is fixed here, so category chips (not
  // brand chips) are the meaningful way to narrow the list.
  protected readonly searchQuery = signal('');
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  protected readonly availableCategories = signal<string[]>([]);
  protected readonly selectedCategories = signal<Set<string>>(new Set());
  protected readonly priceMin = signal<number | null>(null);
  protected readonly priceMax = signal<number | null>(null);
  protected readonly hasActiveFilters = signal(false);

  // See category-page.ts: the product modal is bound to this page's own
  // ?product= query param. Navigating to /product/:id used to leave the brand
  // page and land on the home page when the modal closed.
  protected readonly selectedDeal = signal<Deal | null>(null);

  constructor() {
    effect(() => this.setFaqJsonLd());
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const slug = params.get('brandSlug') ?? '';
      this.loadBrand(slug, params.get('categorySlug'));
    });

    this.route.queryParamMap.subscribe((params) => {
      // The page number comes FROM THE URL (see category-page.ts).
      const page = pageFromQuery(params.get('page'));
      const retailers = params.get('seller') === 'retailers';
      if (page !== this.currentPage() || retailers !== this.retailerView()) {
        this.currentPage.set(page);
        this.retailerView.set(retailers);
        // Before the brand resolves, loadBrand does the load.
        if (this.brandName()) {
          this.loadItems();
          this.setMeta(this.brandName());
        }
      }

      const idParam = params.get('product');
      if (!idParam) {
        this.selectedDeal.set(null);
        return;
      }

      const id = Number(idParam);
      const alreadyLoaded = this.items().find((d) => d.productId === id);
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

  private loadBrand(slug: string, categorySlug: string | null): void {
    this.loading.set(true);
    this.viewMode.set('all');
    // The visitor may arrive directly on "?page=3"; hard-coding 1 silently
    // dropped that address to page 1.
    this.currentPage.set(pageFromQuery(this.route.snapshot.queryParamMap.get('page')));
    this.retailerView.set(this.route.snapshot.queryParamMap.get('seller') === 'retailers');
    this.retailerSamples.set([]);
    this.retailerTotal.set(0);
    this.brandHasOwnStore.set(false);
    this.searchQuery.set('');
    this.selectedCategories.set(new Set());
    this.priceMin.set(null);
    this.priceMax.set(null);
    this.hasActiveFilters.set(false);
    this.fixedCategory.set(null);
    this.fixedCategoryLabel.set('');
    this.brandStats.set(null);
    this.categoryPriceStats.set(null);

    this.dealsService.getFilterOptions().subscribe({
      next: (options) => {
        // The address is a slug ("transparent-labs"), matched to a brand name.
        // resolveBrandFromSlug slugs its input too, so old addresses with
        // spaces still resolve.
        const match = resolveBrandFromSlug(slug, options.brands);
        if (!match) {
          showNotFound(this.router);
          return;
        }

        // On an intersection page the category must be valid too; a made-up
        // slug redirects to the brand's own page instead of returning 200.
        if (categorySlug) {
          if (!options.categories.includes(categorySlug)) {
            this.router.navigate(['/brand', brandSlug(slug)]);
            return;
          }
          this.fixedCategory.set(categorySlug);
          this.fixedCategoryLabel.set(CATEGORY_LABELS[categorySlug] ?? categorySlug);
          this.selectedCategories.set(new Set([categorySlug]));
        }

        this.brandName.set(match);
        // Brand pages linked to no other brand pages; the list without the
        // current brand is kept for internal linking.
        this.otherBrands.set(options.brands.filter((b) => b !== match));

        // Summary of retailer listings; not needed inside the retailer view,
        // which lists them anyway. On error the block stays hidden: a missing
        // section beats a wrong one.
        if (!this.retailerView()) {
          this.dealsService
            .getAllProducts({
              brands: [match],
              sellers: [RETAILER_LABEL],
              categories: categorySlug ? [categorySlug] : [],
              page: 1,
              pageSize: RETAILER_SAMPLE_COUNT,
            })
            .subscribe({
              next: (result) => {
                this.retailerSamples.set(result.items);
                this.retailerTotal.set(result.totalCount);
              },
              error: () => {
                this.retailerSamples.set([]);
                this.retailerTotal.set(0);
              },
            });
        }
        this.availableCategories.set(options.categories);
        this.setMeta(match);

        // Categories where this brand really has products; the intersection
        // links come from here, never to an empty combination.
        this.dealsService.getBrandCategoryPairs().subscribe({
          next: (pairs) => {
            this.brandCategories.set(
              pairs
                .filter((p) => p.brandName === match && p.category !== this.fixedCategory())
                .map((p) => ({ slug: p.category, label: CATEGORY_LABELS[p.category] ?? p.category, count: p.productCount })),
            );
          },
          error: () => this.brandCategories.set([]),
        });

        this.couponsService.getCoupons().subscribe((coupons) => {
          this.coupons.set(coupons.filter((c) => c.brandName === match));
        });

        // Brand-wide statistics on the brand page, ONLY that category's on the
        // intersection: brand-wide numbers there would mislead, and the
        // category-specific ones are that page's only original content.
        this.dealsService.getBrandStats(match, categorySlug ?? undefined).subscribe({
          next: (stats) => this.brandStats.set(stats),
          error: () => this.brandStats.set(null),
        });

        // The category as a whole, to state the brand's price position in it.
        if (categorySlug) {
          this.dealsService.getCategoryPriceStats(categorySlug).subscribe({
            next: (stats) => this.categoryPriceStats.set(stats),
            error: () => this.categoryPriceStats.set(null),
          });
        }

        this.loadItems();
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadItems(): void {
    const brand = this.brandName();
    if (!brand) return;

    this.loading.set(true);
    this.itemsError.set(false);
    this.hasActiveFilters.set(
      this.selectedCategories().size > 0 || this.priceMin() !== null || this.priceMax() !== null || !!this.searchQuery().trim(),
    );
    const retailers = this.retailerView();
    const query = {
      brands: [brand],
      // The brand page is the brand's storefront: when it sells a product
      // itself, the retailer copy isn't listed here (see
      // DealsQuery.preferBrandStore). The retailer view wants the OPPOSITE,
      // so the storefront preference is off and the seller filter is on (the
      // backend skips preferBrandStore when a seller filter is present).
      preferBrandStore: !retailers,
      sellers: retailers ? [RETAILER_LABEL] : [],
      categories: [...this.selectedCategories()],
      search: this.searchQuery().trim() || undefined,
      minPrice: this.priceMin(),
      maxPrice: this.priceMax(),
      page: this.currentPage(),
      pageSize: PAGE_SIZE,
      sortBy: this.sortBy() || undefined,
    };
    const request$ =
      this.viewMode() === 'deals'
        ? this.dealsService.getDeals(query)
        : this.viewMode() === 'store'
          ? this.dealsService.getStoreDeals(query)
          : this.dealsService.getAllProducts(query);

    this.listRequest.run(request$, {
      next: (result) => {
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.totalPages.set(result.totalPages);
        this.loading.set(false);
        // An out-of-range page must NOT claim itself canonical (see
        // category-page.ts); the page total is only known here.
        if (result.totalPages > 0 && this.currentPage() > result.totalPages) {
          this.setMeta(this.brandName());
        }

        // Does the brand have its own store? Read from the list rather than a
        // separate request: with `preferBrandStore` on, a brand with its own
        // products returns records whose seller is ALL empty. No decision on
        // an empty page (the condition is misleadingly true on an empty set).
        if (!retailers && result.items.length > 0) {
          this.brandHasOwnStore.set(result.items.every((d) => !d.seller));
        }

        this.loadSparklines(result.items);
      },
      error: () => {
        this.itemsError.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadSparklines(items: Deal[]): void {
    this.sparklines.set(new Map());
    const ids = items.map((d) => d.productId);
    this.sparklineRequest.run(this.dealsService.getSparklines(ids), {
      next: (result) => this.sparklines.set(new Map(result.map((s) => [s.productId, s.points]))),
    });
  }

  protected sparklineFor(productId: number): PricePoint[] {
    return this.sparklines().get(productId) ?? [];
  }

  protected setViewMode(mode: ViewMode): void {
    if (this.viewMode() === mode) return;
    this.viewMode.set(mode);
    this.backToFirstPage();
  }

  protected onSortChange(value: string): void {
    this.sortBy.set(value);
    this.backToFirstPage();
  }

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
    if (this.searchDebounceHandle) clearTimeout(this.searchDebounceHandle);
    this.searchDebounceHandle = setTimeout(() => {
      this.backToFirstPage();
    }, SEARCH_DEBOUNCE_MS);
  }

  protected toggleCategory(category: string): void {
    const current = new Set(this.selectedCategories());
    current.has(category) ? current.delete(category) : current.add(category);
    this.selectedCategories.set(current);
    this.backToFirstPage();
  }

  protected onPriceMinChange(value: number | null): void {
    this.priceMin.set(value);
    this.backToFirstPage();
  }

  protected onPriceMaxChange(value: number | null): void {
    this.priceMax.set(value);
    this.backToFirstPage();
  }

  protected clearFilters(): void {
    this.selectedCategories.set(new Set());
    this.priceMin.set(null);
    this.priceMax.set(null);
    this.searchQuery.set('');
    this.backToFirstPage();
  }

  protected categoryLabel(slug: string): string {
    return CATEGORY_LABELS[slug] ?? slug;
  }

  /**
   * Back to page one when filters, search or sort change, and CLEAR `page`
   * in the URL (see category-page.ts): otherwise state and URL diverge and
   * the page links go dead.
   */
  private backToFirstPage(): void {
    this.currentPage.set(1);
    if (this.route.snapshot.queryParamMap.get('page')) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { page: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
    this.loadItems();
  }

  /**
   * Query string for a pagination link.
   *
   * `seller` MUST be kept: in the first version, page 2 of the retailer view
   * went back to the brand storefront, with the wrong content and the
   * retailer listings' crawl chain broken on page 1 (the chain is why the
   * view exists). `product` is dropped on purpose: changing page with an open
   * modal makes no sense and would create needless addresses, so no "merge".
   */
  protected pageQuery(page: number): Record<string, string | null> {
    return {
      page: page <= 1 ? null : String(page),
      seller: this.retailerView() ? 'retailers' : null,
    };
  }

  /** Back to the top of the list after a pagination click (routerLink navigates). */
  protected scrollToListTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  private setMeta(brand: string): void {
    const category = this.fixedCategory();
    const brandSlugValue = brandSlug(brand);
    const year = new Date().getFullYear();

    // The intersection ("Transparent Labs Protein Powder Prices") and the
    // brand coupon page target different search intents, so title,
    // description and canonical differ.
    // Paginated addresses are their OWN canonical, with distinct titles.
    const total = this.totalPages();
    const page = total > 0 && this.currentPage() > total ? 1 : this.currentPage();
    const retailers = this.retailerView();
    // The retailer view is a SEPARATE page with its own title and canonical.
    // Pointing it at the storefront would mark it a duplicate and its
    // retailer products would go uncrawled, which is why the view exists.
    const queryParts = [retailers ? 'seller=retailers' : '', page > 1 ? `page=${page}` : ''].filter(Boolean);
    const query = queryParts.length > 0 ? `?${queryParts.join('&')}` : '';

    if (category) {
      const label = this.fixedCategoryLabel();
      this.pageMeta.set({
        title: paginatedTitle(
          retailers
            ? `${brand} ${label} Retailer Prices ${year} | ${SITE_NAME}`
            : `${brand} ${label} Prices and Deals ${year} | ${SITE_NAME}`,
          page,
        ),
        description: retailers
          ? `Retailers selling ${brand} ${label.toLowerCase()} and their current prices. Compare the price difference between sellers for the same product on one page.`
          : `${brand} ${label.toLowerCase()} products, current prices and verified discounts based on real price history, on one page.`,
        canonicalPath: `/brand/${brandSlugValue}/${category}${query}`,
      });
      this.breadcrumbEl = upsertJsonLdScript(
        this.document,
        this.breadcrumbEl,
        buildBreadcrumbJsonLd(this.document, [
          { name: 'Home', path: '/' },
          { name: brand, path: `/brand/${brandSlugValue}` },
          { name: label, path: `/brand/${brandSlugValue}/${category}` },
        ]),
      );
      return;
    }

    const title = paginatedTitle(
      retailers
        ? `${brand} Prices at Retailers ${year} | ${SITE_NAME}`
        : `${brand} Coupon Codes and Deals ${year} | ${SITE_NAME}`,
      page,
    );
    const description = retailers
      ? `Retailers selling ${brand} and their current prices. Compare the price difference between sellers for the same product on one page.`
      : `Current ${brand} coupon codes and verified discounts based on real price history. ${SITE_NAME} tracks ${brand} prices every day.`;

    this.pageMeta.set({
      title,
      description,
      canonicalPath: `/brand/${brandSlugValue}${query}`,
    });
    this.breadcrumbEl = upsertJsonLdScript(
      this.document,
      this.breadcrumbEl,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: brand, path: `/brand/${brandSlugValue}` },
      ]),
    );
  }

  // Built from the same content as the visible FAQ (as on category pages);
  // the Q&A shown to search engines must really be readable on the page.
  //
  // Bound with an effect because the questions depend on coupons and brand
  // stats, which both arrive AFTER the page meta is written; a one-off call
  // would build the schema from still-empty data.
  private setFaqJsonLd(): void {
    const faqs = this.fixedCategory() ? this.brandCategoryFaqs() : this.brandFaqs();
    if (faqs.length === 0) {
      // Don't leave the previous schema behind when moving to an intersection page.
      this.faqEl?.remove();
      this.faqEl = null;
      return;
    }

    this.faqEl = upsertJsonLdScript(this.document, this.faqEl, {
      '@context': 'https://schema.org',
      '@type': 'FAQPage',
      mainEntity: faqs.map((faq) => ({
        '@type': 'Question',
        name: faq.question,
        acceptedAnswer: { '@type': 'Answer', text: faq.answer },
      })),
    });
  }

  protected discountBadge(deal: Deal): string {
    return `-${deal.discountPercent}%`;
  }

  protected storeDiscountBadge(deal: Deal): string {
    return `Store -${deal.storeDiscountPercent}%`;
  }

  // Card links must be real <a href> (see core/product-link.ts). Here the
  // modal opens through ?product= without leaving the page: crawlers see the
  // canonical product address, visitors open the modal in place.
  protected productHref(deal: Deal): string {
    return productHref(deal);
  }

  protected onProductClick(event: MouseEvent, deal: Deal): void {
    // The card has its own click handler; don't fire twice.
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

  // Only with real serving data; no estimated number (see core/value-metrics.ts).
  protected perServing(deal: Deal): number | null {
    return pricePerServing(deal);
  }

  // Alphabetical order gives one canonical URL (a-vs-b, never b-vs-a);
  // otherwise the same content would be reachable at two URLs.
  protected comparisonPairSlug(otherBrand: string): string {
    // Brand names with spaces put %20 addresses into the sitemap; slugs use dashes.
    const current = brandSlug(this.brandName());
    const other = brandSlug(otherBrand);
    return [current, other].sort().join('-vs-');
  }
}
