import { DOCUMENT, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { canonicalOrigin } from '../core/canonical-link';
import { CATEGORY_FAQS, FaqItem } from '../core/category-faqs';
import { CATEGORY_GUIDES, CategoryGuide } from '../core/category-guides';
import { CATEGORY_INTROS, CATEGORY_LABELS } from '../core/category-labels';
import { Deal } from '../core/deal.model';
import { productPath, shouldHandleInApp } from '../core/product-link';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { pageFromQuery, paginatedTitle, pageWindow } from '../core/pagination-window';
import { PriceHistoryService } from '../core/price-history.service';
import { PricePipe } from '../core/price.pipe';
import { formatRelativeTime } from '../core/relative-time';
import { SITE_NAME } from '../core/site-identity';
import { ProductModal } from '../product-modal/product-modal';
import { SiteHeader } from '../site-header/site-header';
import { showNotFound } from '../core/not-found-navigation';

type ViewMode = 'deals' | 'store' | 'all';
const PAGE_SIZE = 24;
const SEARCH_DEBOUNCE_MS = 350;

@Component({
  selector: 'app-category-page',
  imports: [PricePipe, DecimalPipe, RouterLink, FormsModule, ProductModal, SiteHeader],
  templateUrl: './category-page.html',
})
export class CategoryPage implements OnInit {
  protected readonly displayName = displayName;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dealsService = inject(DealsService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private readonly priceHistoryService = inject(PriceHistoryService);

  protected readonly categorySlug = signal<string>('');
  protected readonly categoryLabel = signal<string>('');
  protected readonly categoryIntro = signal<string>('');

  // Category-SPECIFIC questions: the home page FAQ explains the platform;
  // these are about the product and target real search phrases (see
  // category-faqs.ts), so the page is more than a product list.
  protected readonly faqItems = signal<FaqItem[]>([]);
  private faqStructuredDataEl: HTMLScriptElement | null = null;
  private breadcrumbEl: HTMLScriptElement | null = null;
  // Long-form guide (see core/category-guides.ts); null for categories
  // without one, and the page then hides the section.
  protected readonly categoryGuide = signal<CategoryGuide | null>(null);
  private speakableEl: HTMLScriptElement | null = null;
  protected readonly otherCategories = signal<{ slug: string; label: string }[]>([]);
  protected readonly loading = signal(true);
  // Only set when the /api/filters request fails; an invalid slug is handled
  // by the not-found page.
  protected readonly loadError = signal(false);
  protected readonly itemsError = signal(false);

  // The category page once showed only discounted products, hiding regular
  // prices entirely. The home page's tabs and pagination apply here too, so
  // every product in a category can be seen.
  protected readonly viewMode = signal<ViewMode>('all');
  protected readonly items = signal<Deal[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  protected readonly currentPage = signal(1);
  // Page numbers for the pagination bar (null = "…").
  protected readonly pageItems = computed(() => pageWindow(this.currentPage(), this.totalPages()));
  protected readonly sortBy = signal<string>('');
  // Search within the category. The backend already supported `search`
  // (the same query as deals-list.ts); it just wasn't wired up here.
  protected readonly searchQuery = signal('');
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  // Brand chips and a price range. The category is fixed on this page, so
  // brand chips narrow the list meaningfully.
  protected readonly availableBrands = signal<string[]>([]);
  protected readonly selectedBrands = signal<Set<string>>(new Set());
  protected readonly priceMin = signal<number | null>(null);
  protected readonly priceMax = signal<number | null>(null);
  protected readonly hasActiveFilters = signal(false);

  // Product modal: the same pattern as deals-list.ts but with a ?product=
  // query param instead of a path param. This page's canonical URL is
  // /category/:slug, and the modal is a state of this page, not another page.
  // Navigating to /product/:id used to leave the category page entirely and
  // land on the home page when the modal closed (a real bug).
  protected readonly selectedDeal = signal<Deal | null>(null);

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const slug = params.get('categorySlug') ?? '';
      this.loadCategory(slug);
    });

    this.route.queryParamMap.subscribe((params) => {
      // The page number comes FROM THE URL. As a component-only signal,
      // "?page=3" always showed page 1 (measured: identical products).
      const page = pageFromQuery(params.get('page'));
      if (page !== this.currentPage()) {
        this.currentPage.set(page);
        // Before the category resolves, loadCategory does the load; no
        // second request here.
        if (this.categorySlug()) {
          this.loadItems();
          this.setMeta(this.categoryLabel(), this.categorySlug());
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

  private loadCategory(slug: string): void {
    this.loading.set(true);
    this.viewMode.set('all');
    // The visitor may arrive directly on "?page=3" (a search result, a shared
    // link, a crawler); hard-coding 1 silently dropped that address to page 1.
    this.currentPage.set(pageFromQuery(this.route.snapshot.queryParamMap.get('page')));
    this.searchQuery.set('');
    this.selectedBrands.set(new Set());
    this.priceMin.set(null);
    this.priceMax.set(null);
    this.hasActiveFilters.set(false);

    this.dealsService.getFilterOptions().subscribe({
      next: (options) => {
        const match = options.categories.find((c) => c.toLowerCase() === slug.toLowerCase());
        const label = match ? CATEGORY_LABELS[match] : undefined;
        if (!match || !label) {
          showNotFound(this.router);
          return;
        }

        this.categorySlug.set(match);
        this.categoryLabel.set(label);
        this.categoryIntro.set(CATEGORY_INTROS[match]);
        this.categoryGuide.set(CATEGORY_GUIDES[match] ?? null);
        this.setFaq(match);
        this.otherCategories.set(
          options.categories
            .filter((c) => c !== match)
            .map((c) => ({ slug: c, label: CATEGORY_LABELS[c] ?? c })),
        );
        this.availableBrands.set(options.brands);
        this.setMeta(label, match);

        this.loadItems();
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadItems(): void {
    const categorySlug = this.categorySlug();
    if (!categorySlug) return;

    this.loading.set(true);
    this.itemsError.set(false);
    this.hasActiveFilters.set(
      this.selectedBrands().size > 0 || this.priceMin() !== null || this.priceMax() !== null || !!this.searchQuery().trim(),
    );
    const query = {
      categories: [categorySlug],
      brands: [...this.selectedBrands()],
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

    request$.subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.totalPages.set(result.totalPages);
        this.loading.set(false);
        // An out-of-range page (a shrunk catalog, a typed address) must NOT
        // claim itself canonical: calling an empty page valid feeds "Crawled -
        // currently not indexed". The page total is only known here, not at
        // the first setMeta call, so it is called again when needed.
        if (result.totalPages > 0 && this.currentPage() > result.totalPages) {
          this.setMeta(this.categoryLabel(), this.categorySlug());
        }
      },
      error: () => {
        this.itemsError.set(true);
        this.loading.set(false);
      },
    });
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

  protected toggleBrand(brand: string): void {
    const current = new Set(this.selectedBrands());
    current.has(brand) ? current.delete(brand) : current.add(brand);
    this.selectedBrands.set(current);
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
    this.selectedBrands.set(new Set());
    this.priceMin.set(null);
    this.priceMax.set(null);
    this.searchQuery.set('');
    this.backToFirstPage();
  }

  /**
   * Back to page one when filters, search or sort change.
   *
   * The URL's `page` MUST be cleared too: otherwise the component shows page
   * 1 while the URL still says "?page=7", and clicking the link for page 7
   * does nothing, since `queryParamMap` doesn't emit for an unchanged value.
   */
  private backToFirstPage(): void {
    this.currentPage.set(1);
    if (this.route.snapshot.queryParamMap.get('page')) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { page: null },
        queryParamsHandling: 'merge',
        // Each filter click should not become its own "back" stop.
        replaceUrl: true,
      });
    }
    this.loadItems();
  }

  /**
   * Scrolls to the top of the list after a pagination click. The navigation
   * itself is the `routerLink` (see the template); the list loads correctly
   * without this method.
   */
  protected scrollToListTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // The visible FAQ plus FAQPage structured data from the same content, which
  // Google can show as expandable Q&A in results (as on the home page).
  private setFaq(slug: string): void {
    const items = CATEGORY_FAQS[slug] ?? [];
    this.faqItems.set(items);

    if (items.length === 0) {
      this.faqStructuredDataEl?.remove();
      this.faqStructuredDataEl = null;
      return;
    }

    this.faqStructuredDataEl = upsertJsonLdScript(this.document, this.faqStructuredDataEl, {
      '@context': 'https://schema.org',
      '@type': 'FAQPage',
      mainEntity: items.map((item) => ({
        '@type': 'Question',
        name: item.question,
        acceptedAnswer: { '@type': 'Answer', text: item.answer },
      })),
    });
  }

  private setMeta(label: string, slug: string): void {
    // Paginated addresses are their OWN canonical. Pointing them at page 1
    // is explicitly discouraged by Google: the series counts as duplicates,
    // is crawled less, and the deep products we want reachable stay out of
    // reach. The title must differ too, or hundreds of addresses share one.
    const total = this.totalPages();
    const page = total > 0 && this.currentPage() > total ? 1 : this.currentPage();
    const year = new Date().getFullYear();
    const title = paginatedTitle(`${label} Prices and Deals ${year} | ${SITE_NAME}`, page);
    const description = `Current ${label.toLowerCase()} prices, verified discounts based on real price history, and store sales. ${SITE_NAME} tracks prices every day.`;

    this.pageMeta.set({
      title,
      description,
      canonicalPath: page > 1 ? `/category/${slug}?page=${page}` : `/category/${slug}`,
    });

    this.breadcrumbEl = upsertJsonLdScript(
      this.document,
      this.breadcrumbEl,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Categories', path: '/categories' },
        { name: label, path: `/category/${slug}` },
      ]),
    );

    // Categories with a long-form guide get a "speakable" section that AI and
    // voice assistants can quote directly.
    if (CATEGORY_GUIDES[slug]) {
      this.speakableEl = upsertJsonLdScript(this.document, this.speakableEl, {
        '@context': 'https://schema.org',
        '@type': 'WebPage',
        speakable: { '@type': 'SpeakableSpecification', cssSelector: ['#zero-click-answer'] },
        url: `${canonicalOrigin(this.document)}/category/${slug}`,
      });
    } else {
      this.speakableEl?.remove();
      this.speakableEl = null;
    }
  }

  protected discountBadge(deal: Deal): string {
    return `-${deal.discountPercent}%`;
  }

  protected storeDiscountBadge(deal: Deal): string {
    return `Store -${deal.storeDiscountPercent}%`;
  }

  // Row links must be real <a href> (see core/product-link.ts). Here the
  // modal opens through ?product= without leaving the page, so a real href
  // plus a controlled click is used instead of RouterLink: crawlers see the
  // canonical product address, visitors open the modal in place.
  protected productPath(deal: Deal): string {
    return productPath(deal);
  }

  protected onProductClick(event: MouseEvent, deal: Deal): void {
    // The row has its own click handler; don't fire twice.
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
