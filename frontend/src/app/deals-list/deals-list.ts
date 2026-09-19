import { DOCUMENT, DecimalPipe, Location, isPlatformServer } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, OnInit, PLATFORM_ID, RESPONSE_INIT, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';

import { filterSelectValue, readFilterSelection } from '../core/filter-select';
import { buildProductJsonLdDescription, offerAvailability } from '../core/product-facts';
import { ArticleSummary } from '../core/article.model';
import { ArticlesService } from '../core/articles.service';
import { canonicalOrigin } from '../core/canonical-link';
import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { CATEGORY_LABELS } from '../core/category-labels';
import { CATEGORY_ICON_PATHS, DEFAULT_CATEGORY_ICON, categoryPhosphorIcon } from '../core/nav-icons';
import { ComparisonService } from '../core/comparison.service';
import { Coupon } from '../core/coupon.model';
import { CouponsService } from '../core/coupons.service';
import { Deal } from '../core/deal.model';
import { DealsQuery, DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { FavoritesService } from '../core/favorites.service';
import { HomepageStats } from '../core/homepage-stats.model';
import { MARKET } from '../core/market';
import { pricePerServing } from '../core/value-metrics';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { paginatedTitle, pageWindow } from '../core/pagination-window';
import { PricePoint } from '../core/price-history.model';
import { PriceHistoryService } from '../core/price-history.service';
import { PricePipe } from '../core/price.pipe';
import { PwaInstallService } from '../core/pwa-install.service';
import { formatRelativeTime } from '../core/relative-time';
import { SITE_NAME } from '../core/site-identity';
import { slugify } from '../core/slugify';
import { productPath } from '../core/product-link';
import { buildPageTitle, buildProductDescription, formatPriceText } from '../core/meta-description';
import { buildAreaPath, buildLinePath, toCoordinates } from '../core/spark-chart';
import { SubscribeService } from '../core/subscribe.service';
import { ThemePreference, ThemeService } from '../core/theme.service';
import { ProductCardSparkline } from '../product-card-sparkline/product-card-sparkline';
import { ProductModal } from '../product-modal/product-modal';
import { PreferredProducts } from '../preferred-products/preferred-products';
import { EditionSwitcher } from '../edition-switcher/edition-switcher';
import { showNotFound } from '../core/not-found-navigation';

type ViewMode = 'deals' | 'all' | 'store';

// Only a view that DIFFERS from the default is written to the URL; this
// constant must be the same where the URL is written and where it is read.
const DEFAULT_VIEW_MODE: ViewMode = 'store';

const isMac = typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);
const PAGE_SIZE = 24;
const SEARCH_DEBOUNCE_MS = 350;

/** A single selection shows its own name; several show a count. */
function selectionLabel(selected: Set<string>, single: (name: string) => string, many: (count: number) => string): string {
  const [first] = selected;
  return selected.size === 1 ? single(first) : many(selected.size);
}

// Small price chart on the hero card, much smaller than the product modal's.
const HERO_CHART = { width: 280, height: 90, paddingY: 8 };

const SCAN_TIME_FORMATTER = new Intl.DateTimeFormat(MARKET.locale, { hour: 'numeric', minute: '2-digit', timeZone: MARKET.timeZone });
const SCAN_DATE_FORMATTER = new Intl.DateTimeFormat(MARKET.locale, { month: 'long', day: 'numeric', timeZone: MARKET.timeZone });

// ORDER MATTERS: keywords first, brand at the end. clampTitle drops whatever
// follows the last " | " when a title is too long (it assumes a trailing
// brand suffix); with the brand first, the Turkish site's home page was once
// left titled with the brand alone.
const DEFAULT_TITLE = `Real Protein and Supplement Deals | ${SITE_NAME}`;
const DEFAULT_DESCRIPTION =
  'WheyProof tracks protein powder, creatine, pre-workout and other supplement prices every day and shows which discounts are real, based on price history rather than the store\'s own "was" price.';

// Real questions only, each describing something the site already does; no
// marketing claims. Also used for the FAQPage structured data.
const FAQ_ITEMS: { question: string; answer: string }[] = [
  {
    question: 'What is the difference between "Real price drops" and "Store sales"?',
    answer:
      '"Real price drops" are based on the price history we collect: a product is listed when its current price is below its highest price in the last 30 days. "Store sales" show the old and new prices a store displays on its own site, which we have not verified yet.',
  },
  {
    question: 'How often are prices updated?',
    answer: 'The stores we track are checked automatically four times a day, and prices update as they change.',
  },
  {
    question: 'Do I buy the product from WheyProof?',
    answer:
      'No. WheyProof is a price tracker, not a store. "Go to store" takes you straight to the brand\'s or retailer\'s own site, and the purchase happens there.',
  },
  {
    question: 'Where do the coupon codes come from?',
    answer:
      'Coupon codes are not collected automatically. To avoid showing expired or wrong codes, we only publish codes we have checked by hand.',
  },
  {
    question: 'Why do some products show no price per serving?',
    answer:
      'We only show it when the brand publishes the serving size and the package weight; we never show an estimated number.',
  },
];

@Component({
  selector: 'app-deals-list',
  imports: [PricePipe, DecimalPipe, FormsModule, PreferredProducts, ProductCardSparkline, ProductModal, RouterLink, EditionSwitcher],
  templateUrl: './deals-list.html',
})
export class DealsList implements OnInit {
  // Turns ALL CAPS product names into readable Title Case in the template.
  protected readonly displayName = displayName;
  private readonly dealsService = inject(DealsService);
  private readonly couponsService = inject(CouponsService);
  private readonly articlesService = inject(ArticlesService);
  private readonly favoritesService = inject(FavoritesService);
  // The comparison selection is shared at service level; the bottom bar and
  // the other pages read the same signal.
  protected readonly comparison = inject(ComparisonService);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly subscribeService = inject(SubscribeService);
  protected readonly pwaInstall = inject(PwaInstallService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  // In-app navigations in this session. Closing the modal goes BACK only if
  // there is one (see closeDeal). A flag set in openDeal did not work: product
  // cards use routerLink, so openDeal never ran; that was found by measuring
  // the history length in the browser.
  private inAppNavigations = 0;
  /** Tells the first queryParamMap emission in ngOnInit apart. */
  private initialLoadDone = false;
  private readonly destroyRef = inject(DestroyRef);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  // Sets the real HTTP status during SSR (see productLoadError). Only present
  // on the server, null in the browser, hence the optional injection.
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });
  private readonly isServer = isPlatformServer(inject(PLATFORM_ID));
  protected readonly theme = inject(ThemeService);
  private readonly searchInput = viewChild<{ nativeElement: HTMLInputElement }>('searchInput');
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private structuredDataEl: HTMLScriptElement | null = null;
  private faqStructuredDataEl: HTMLScriptElement | null = null;
  private breadcrumbEl: HTMLScriptElement | null = null;

  protected readonly shortcutLabel = isMac ? '⌘K' : 'Ctrl+K';

  protected readonly deals = signal<Deal[]>([]);
  // Mini sparklines on the product cards, filled by one batched request per
  // page load (see loadSparklines), not one request per card.
  protected readonly sparklines = signal<Map<number, PricePoint[]>>(new Map());
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  // The default tab is "store" on purpose: "Real price drops" is mostly empty
  // while the price history is young, and an empty first screen breaks trust.
  // Store sales are always populated and honestly labeled as unverified.
  protected readonly viewMode = signal<ViewMode>('store');
  protected readonly selectedDeal = signal<Deal | null>(null);
  // Only a REAL 404 from the backend shows the not-found page. A temporary
  // problem (network error, backend 5xx) sets this instead: the page does not
  // redirect and SSR returns 503 ("try again"), never a "gone for good"
  // signal. Redirecting on every error once made Google drop product pages
  // after a brief backend outage.
  protected readonly productLoadError = signal(false);

  // /product/:id shares this component with the home page (route reuse). On
  // the server, while a product is selected, the home page content behind the
  // modal is left out of the HTML: otherwise product pages were ~92% identical
  // to each other. In the browser nothing changes (the modal covers the whole
  // screen). The H1 is always rendered.
  protected readonly showFullHomepageContent = computed(() => !this.isServer || !this.selectedDeal());

  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  protected readonly currentPage = signal(1);

  protected readonly searchQuery = signal('');
  protected readonly selectedBrands = signal<Set<string>>(new Set());
  // Seller filter: the same product can come from the brand's own site and
  // from a retailer (there is no barcode to match them), and people should be
  // able to pick which one they want.
  protected readonly selectedSellers = signal<Set<string>>(new Set());
  protected readonly availableSellers = signal<string[]>([]);
  protected readonly selectedCategories = signal<Set<string>>(new Set());
  protected readonly priceMin = signal<number | null>(null);
  protected readonly priceMax = signal<number | null>(null);
  protected readonly sortBy = signal<string>('');

  // What the select shows: the placeholder ("All brands") without a filter,
  // otherwise a status option. Going back to the empty value clears it.
  protected readonly brandSelectValue = computed(() => filterSelectValue(this.selectedBrands().size));
  protected readonly categorySelectValue = computed(() => filterSelectValue(this.selectedCategories().size));
  protected readonly sellerSelectValue = computed(() => filterSelectValue(this.selectedSellers().size));

  // A single selection shows the NAME, not "1 selected", which confused people.
  protected readonly brandSelectLabel = computed(() =>
    selectionLabel(this.selectedBrands(), (name) => name, (n) => `${n} brands selected`));
  protected readonly categorySelectLabel = computed(() =>
    selectionLabel(this.selectedCategories(), (name) => this.categoryLabel(name), (n) => `${n} categories selected`));
  protected readonly sellerSelectLabel = computed(() =>
    selectionLabel(this.selectedSellers(), (name) => name, (n) => `${n} sellers selected`));

  protected readonly availableBrands = signal<string[]>([]);
  protected readonly availableCategories = signal<string[]>([]);

  // The nav's category dropdown. This page keeps its own nav (it holds the
  // search box) instead of SiteHeader, so the pattern is repeated here.
  protected readonly categoriesOpen = signal(false);

  protected readonly hasActiveFilters = signal(false);

  protected readonly coupons = signal<Coupon[]>([]);

  // Candidate pool for the discovery strip, a deterministic shuffle of the
  // catalog. A separate signal keeps the strip from jumping when the list
  // tabs or filters change.
  protected readonly preferredProductCandidates = signal<Deal[]>([]);

  // "Back to top" button that appears after scrolling down.
  protected readonly showScrollTop = signal(false);

  // Total catalog size for the home page, independent of the current tab or
  // filter, straight from /api/products.
  protected readonly siteProductCount = signal(0);
  protected readonly faqItems = FAQ_ITEMS;

  // Live scan strip, from /api/stats once per page load.
  protected readonly stats = signal<HomepageStats | null>(null);
  protected readonly lastScanLabel = computed(() => {
    const lastScanAt = this.stats()?.lastScanAt;
    if (!lastScanAt) return null;
    const d = new Date(lastScanAt);
    return `${SCAN_TIME_FORMATTER.format(d)} ET · ${SCAN_DATE_FORMATTER.format(d)}`;
  });

  // Watchlist badge, the service's shared signal.
  protected readonly favoritesCount = this.favoritesService.count;

  // Guide teaser: the first three articles.
  protected readonly articles = signal<ArticleSummary[]>([]);

  // "Know first when the price drops" band. Uses the same SubscribeService as
  // the footer's NewsletterSignup, with its own form.
  protected readonly alertEmail = signal('');
  protected readonly alertSubmitting = signal(false);
  protected readonly alertStatusMessage = signal<string | null>(null);

  // Hero: the biggest real discount, independent of the tab and filters;
  // falls back to the biggest store sale so the hero is never empty.
  protected readonly heroDeal = signal<Deal | null>(null);
  protected readonly heroPoints = signal<PricePoint[]>([]);
  protected readonly heroCoordinates = computed(() => {
    const points = this.heroPoints();
    if (points.length === 0) return [];
    const prices = points.map((p) => p.price);
    return toCoordinates(points, Math.min(...prices), Math.max(...prices), HERO_CHART);
  });
  protected readonly heroLinePath = computed(() => buildLinePath(this.heroCoordinates()));
  protected readonly heroAreaPath = computed(() => buildAreaPath(this.heroCoordinates(), HERO_CHART.height));

  constructor() {
    // With the product modal open, title/description/Open Graph describe that
    // product (with SSR, a shared /product/:id link or a search result shows
    // the real product); closing it returns to the site-wide values.
    effect(() => {
      const deal = this.selectedDeal();

      if (!deal) {
        // A paginated home page is its OWN canonical, or "?page=2..N" would
        // count as duplicates and the product links on them would not be
        // crawled, which was the point of crawlable pagination.
        //
        // FILTERED URLs are excluded: brand/category/price/search combinations
        // produce endless addresses, and treating each as a page would scatter
        // the crawl budget. Their canonical stays the root.
        const page = this.currentPage();
        const total = this.totalPages();
        // An out-of-range page must not claim itself as canonical. Cheap here:
        // this is an effect and re-runs once totalPages is known.
        const inRange = total === 0 || page <= total;
        const plainPaginated = page > 1 && inRange && !this.hasActiveFilters();
        this.pageMeta.set({
          title: plainPaginated ? paginatedTitle(DEFAULT_TITLE, page) : DEFAULT_TITLE,
          description: DEFAULT_DESCRIPTION,
          canonicalPath: plainPaginated ? `/?page=${page}` : '/',
        });
        this.structuredDataEl?.remove();
        this.structuredDataEl = null;
        this.breadcrumbEl?.remove();
        this.breadcrumbEl = null;
        // FAQPage JSON-LD only while the home page itself is shown; it used
        // to be added unconditionally and leaked into product pages' SSR HTML.
        this.faqStructuredDataEl = upsertJsonLdScript(this.document, this.faqStructuredDataEl, {
          '@context': 'https://schema.org',
          '@type': 'FAQPage',
          mainEntity: FAQ_ITEMS.map((item) => ({
            '@type': 'Question',
            name: item.question,
            acceptedAnswer: { '@type': 'Answer', text: item.answer },
          })),
        });
        return;
      }

      this.faqStructuredDataEl?.remove();
      this.faqStructuredDataEl = null;

      const displayedName = displayName(deal.productName);
      const priceText = formatPriceText(deal.currentPrice);
      // NO price in the title on purpose: prices change several times a day,
      // and rewriting the title each time makes Google keep re-fetching the
      // snippet. The price stays in the description, the JSON-LD Offer and the
      // share card title (ogTitle).
      const title = buildPageTitle(displayedName, 'Price & Price History', deal.brandName);
      const ogTitle = `${displayedName} Price: ${priceText} | ${deal.brandName} — ${SITE_NAME}`;
      const description = buildProductDescription({
        displayName: displayedName,
        brandName: deal.brandName,
        priceText,
        discountPercent: deal.discountPercent,
        description: deal.description,
      });

      // A duplicate record points its canonical at the MAIN page. The slug is
      // the same (they share the product name), only the id differs.
      const canonicalId = deal.canonicalProductId ?? deal.productId;
      const canonicalProductPath = `/product/${canonicalId}/${slugify(deal.productName)}`;

      this.pageMeta.set({
        title,
        ogTitle,
        description,
        canonicalPath: canonicalProductPath,
        // og:type 'product' needs catalog fields we do not fill; the product
        // signal is the JSON-LD Product/Offer below.
        ogType: 'website',
        ogImage: deal.imageUrl ?? undefined,
        // A product the store stopped returning, with no current replacement:
        // the page keeps working (its price history still has value and shared
        // links do not break) but stays out of the index, since no list links
        // to it any more.
        noIndex: deal.isStale === true,
      });

      // schema.org Product/Offer. "availability" only when the store reported
      // stock (inStock is three-state): claiming "InStock" without data is
      // worse than leaving the field out, but a known value is what Search
      // Console asked for (2026-09-15, non-critical).
      const jsonLd = {
        '@context': 'https://schema.org',
        '@type': 'Product',
        name: displayedName,
        sku: String(deal.productId),
        ...(deal.imageUrl ? { image: deal.imageUrl } : {}),
        brand: { '@type': 'Brand', name: deal.brandName },
        // Written from our own measurements, not the brand's marketing copy.
        description: buildProductJsonLdDescription(deal),
        offers: {
          '@type': 'Offer',
          url: `${canonicalOrigin(this.document)}${canonicalProductPath}`,
          priceCurrency: MARKET.currency,
          price: deal.currentPrice.toFixed(2),
          ...offerAvailability(deal.inStock),
        },
        // The store's own customer rating, ONLY when it exists, and shown on
        // the page too: Google requires marked-up ratings to be visible.
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
      };

      this.structuredDataEl = upsertJsonLdScript(this.document, this.structuredDataEl, jsonLd);

      const categoryLabel = deal.category ? (CATEGORY_LABELS[deal.category] ?? deal.category) : null;
      this.breadcrumbEl = upsertJsonLdScript(
        this.document,
        this.breadcrumbEl,
        buildBreadcrumbJsonLd(this.document, [
          { name: 'Home', path: '/' },
          ...(categoryLabel && deal.category ? [{ name: categoryLabel, path: `/category/${deal.category}` }] : []),
          { name: deal.productName, path: canonicalProductPath },
        ]),
      );
    });
  }

  ngOnInit(): void {
    this.dealsService.getFilterOptions().subscribe((options) => {
      this.availableBrands.set(options.brands);
      this.availableCategories.set(options.categories);
      this.availableSellers.set(options.sellers ?? []);
    });
    this.couponsService.getCoupons().subscribe((coupons) => this.coupons.set(coupons));
    this.dealsService.getPreferredProducts(60).subscribe({
      next: (products) => this.preferredProductCandidates.set(products),
      error: () => this.preferredProductCandidates.set([]),
    });
    // pageSize 1: only the total count is needed.
    this.dealsService.getAllProducts({ pageSize: 1 }).subscribe((result) => this.siteProductCount.set(result.totalCount));
    this.dealsService.getStats().subscribe((stats) => this.stats.set(stats));
    this.favoritesService.ensureCount();
    this.articlesService.getArticles().subscribe((articles) => this.articles.set(articles.slice(0, 3)));
    this.loadHeroDeal();
    // The first load is deliberately NOT here but inside the queryParamMap
    // subscription below, which fires synchronously once, so ?search= or
    // ?page= apply to the very first request. Calling load() here too
    // started two parallel requests, and the unfiltered one could overwrite
    // the filtered result.

    // The whole list state lives in the URL, so the browser's back/forward
    // buttons (mouse side buttons too) and shared links work. This
    // subscription mainly reacts when the URL changes FROM OUTSIDE; it does
    // not reload when nothing differs.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      // Searches from the header on other pages arrive here as ?search=.
      const search = (params.get('search') ?? '').trim();
      // The first emission must always load: the values are equal then, so
      // the comparisons below are false and the list would never fill.
      let needsLoad = !this.initialLoadDone;
      this.initialLoadDone = true;

      if (search !== this.searchQuery()) {
        this.searchQuery.set(search);
        needsLoad = true;
        // A search from outside switches to "All": the default tab is store
        // sales, and a product with no current sale would show an empty list
        // even though results exist. A tab the visitor picked is kept; this
        // only runs when a new search arrives in the URL.
        if (search) this.viewMode.set('all');
      }

      // Filters are restored from the URL too. This is what makes BACK work:
      // returning from a product or comparison rebuilds the component and the
      // signals would fall back to their defaults.
      const readSet = (name: string) => {
        const raw = params.get(name);
        return new Set((raw ?? '').split(',').map((x) => x.trim()).filter(Boolean));
      };
      const readNumber = (name: string) => {
        const raw = params.get(name);
        if (raw === null || raw.trim() === '') return null;
        const value = Number(raw);
        return Number.isFinite(value) ? value : null;
      };

      const setsDiffer = (a: Set<string>, b: Set<string>) =>
        a.size !== b.size || [...a].some((x) => !b.has(x));

      const brands = readSet('brands');
      if (setsDiffer(brands, this.selectedBrands())) {
        this.selectedBrands.set(brands);
        needsLoad = true;
      }

      const categories = readSet('categories');
      if (setsDiffer(categories, this.selectedCategories())) {
        this.selectedCategories.set(categories);
        needsLoad = true;
      }

      const sellers = readSet('sellers');
      if (setsDiffer(sellers, this.selectedSellers())) {
        this.selectedSellers.set(sellers);
        needsLoad = true;
      }

      const min = readNumber('min');
      if (min !== this.priceMin()) {
        this.priceMin.set(min);
        needsLoad = true;
      }

      const max = readNumber('max');
      if (max !== this.priceMax()) {
        this.priceMax.set(max);
        needsLoad = true;
      }

      const sort = params.get('sort') ?? '';
      if (sort !== this.sortBy()) {
        this.sortBy.set(sort);
        needsLoad = true;
      }

      // The view tab changes the list, so it is restored too, but only when
      // the URL states it, so it does not override the "search from outside
      // switches to All" rule above.
      const view = params.get('view') as ViewMode | null;
      if (view && view !== this.viewMode()) {
        this.viewMode.set(view);
        needsLoad = true;
      }

      const page = Math.max(1, Number(params.get('page')) || 1);
      if (page !== this.currentPage()) {
        this.currentPage.set(page);
        needsLoad = true;
      }

      if (needsLoad) this.load();
    });

    // The product modal is bound to the URL (/product/:id). The component
    // lives on between '' and 'product/:id' without being rebuilt (see
    // DealsRouteReuseStrategy), so parameter changes are handled here.
    this.router.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event instanceof NavigationEnd) this.inAppNavigations++;
      });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const idParam = params.get('id');
      if (!idParam) {
        this.selectedDeal.set(null);
        return;
      }

      const id = Number(idParam);
      const slugParam = params.get('slug');

      const alreadyLoaded = this.deals().find((d) => d.productId === id);
      if (alreadyLoaded) {
        this.selectedDeal.set(alreadyLoaded);
        this.ensureCanonicalSlug(alreadyLoaded, slugParam);
        return;
      }

      this.productLoadError.set(false);
      this.dealsService.getProductById(id).subscribe({
        next: (deal) => {
          this.selectedDeal.set(deal);
          this.ensureCanonicalSlug(deal, slugParam);
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 404) {
            showNotFound(this.router);
            return;
          }
          // Temporary problem (network error, backend 5xx): say "could not
          // load right now", not "gone". On the server this is a real 503.
          this.productLoadError.set(true);
          if (this.responseInit) this.responseInit.status = 503;
        },
      });
    });
  }

  // Redirects to the canonical slug with replaceUrl when the slug is missing
  // (old /product/:id links, typed addresses) or stale because the product
  // name changed. During SSR this becomes a real HTTP redirect, which moves
  // the ranking signal of bare /product/:id links to the canonical URL. A
  // correct slug does nothing (no redirect loop).
  private ensureCanonicalSlug(deal: Deal, slugParam: string | null): void {
    // The store moved this record's address and a current record of the
    // same product exists: the old address moves to the current one, so the
    // two pages do not compete in search results.
    if (deal.replacementProductId) {
      this.router.navigate(['/product', deal.replacementProductId, slugify(deal.productName)], {
        replaceUrl: true,
        queryParamsHandling: 'preserve',
      });
      return;
    }

    const canonicalSlug = slugify(deal.productName);
    if (slugParam === canonicalSlug) return;
    this.router.navigate(['/product', deal.productId, canonicalSlug], {
      replaceUrl: true,
      queryParamsHandling: 'preserve',
    });
  }

  protected setViewMode(mode: ViewMode): void {
    if (this.viewMode() === mode) return;
    this.viewMode.set(mode);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
    if (this.searchDebounceHandle) clearTimeout(this.searchDebounceHandle);
    this.searchDebounceHandle = setTimeout(() => {
      this.currentPage.set(1);
      this.load();
      this.syncUrlState(1, false);
    }, SEARCH_DEBOUNCE_MS);
  }

  protected onSortChange(value: string): void {
    this.sortBy.set(value);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected toggleSeller(seller: string): void {
    const current = new Set(this.selectedSellers());
    if (current.has(seller)) current.delete(seller);
    else current.add(seller);
    this.selectedSellers.set(current);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected onSellerSelect(value: string): void {
    const selection = readFilterSelection(value);
    if (selection.kind === 'toggle') this.toggleSeller(selection.value);
    else if (selection.kind === 'clear') this.clearSellers();
  }

  protected toggleBrand(brand: string): void {
    const current = new Set(this.selectedBrands());
    current.has(brand) ? current.delete(brand) : current.add(brand);
    this.selectedBrands.set(current);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected toggleCategory(category: string): void {
    const current = new Set(this.selectedCategories());
    current.has(category) ? current.delete(category) : current.add(category);
    this.selectedCategories.set(current);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected onPriceMinChange(value: number | null): void {
    this.priceMin.set(value);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected onPriceMaxChange(value: number | null): void {
    this.priceMax.set(value);
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected onBrandSelect(value: string): void {
    const selection = readFilterSelection(value);
    if (selection.kind === 'toggle') this.toggleBrand(selection.value);
    else if (selection.kind === 'clear') this.clearBrands();
  }

  protected onCategorySelect(value: string): void {
    const selection = readFilterSelection(value);
    if (selection.kind === 'toggle') this.toggleCategory(selection.value);
    else if (selection.kind === 'clear') this.clearCategories();
  }

  private clearBrands(): void {
    if (this.selectedBrands().size === 0) return;
    this.selectedBrands.set(new Set());
    this.afterFilterChange();
  }

  private clearCategories(): void {
    if (this.selectedCategories().size === 0) return;
    this.selectedCategories.set(new Set());
    this.afterFilterChange();
  }

  private clearSellers(): void {
    if (this.selectedSellers().size === 0) return;
    this.selectedSellers.set(new Set());
    this.afterFilterChange();
  }

  private afterFilterChange(): void {
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  protected clearFilters(): void {
    this.selectedBrands.set(new Set());
    this.selectedSellers.set(new Set());
    this.selectedCategories.set(new Set());
    this.priceMin.set(null);
    this.priceMax.set(null);
    this.searchQuery.set('');
    // Sorting is not strictly a filter, but "clear" should bring the whole
    // list back to its default view; a leftover sort looks like nothing was
    // cleared.
    this.sortBy.set('');
    this.currentPage.set(1);
    this.load();
    this.syncUrlState(1, false);
  }

  /**
   * Page numbers for the pagination bar (null = "…"). With only prev/next
   * links, the last of many pages would sit that many clicks deep. See
   * `core/pagination-window.ts`.
   */
  protected readonly pageItems = computed(() => pageWindow(this.currentPage(), this.totalPages()));

  /**
   * Pagination uses `<a href>` (see the template): routerLink navigates and
   * the URL subscription updates the state; this only scrolls to the top.
   */
  protected scrollToListTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  /**
   * Writes the WHOLE list state to the URL. With only `page` in the URL, a
   * search followed by a visit to a product and BACK landed on an empty,
   * reset home page. Now the browser's own back/forward logic is enough, and
   * the list is shareable and survives a reload.
   *
   * Defaults are NOT written (null drops the parameter), so the unfiltered
   * home page keeps a clean, canonical address.
   */
  private syncUrlState(page: number, push: boolean): void {
    const list = (value: Set<string>) => (value.size > 0 ? [...value].join(',') : null);
    const search = this.searchQuery().trim();

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        page: page > 1 ? page : null,
        search: search || null,
        brands: list(this.selectedBrands()),
        categories: list(this.selectedCategories()),
        sellers: list(this.selectedSellers()),
        min: this.priceMin(),
        max: this.priceMax(),
        sort: this.sortBy() || null,
        view: this.viewMode() === DEFAULT_VIEW_MODE ? null : this.viewMode(),
      },
      queryParamsHandling: 'merge',
      replaceUrl: !push,
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const query: DealsQuery = {
      brands: [...this.selectedBrands()],
      sellers: [...this.selectedSellers()],
      categories: [...this.selectedCategories()],
      search: this.searchQuery().trim() || undefined,
      minPrice: this.priceMin(),
      maxPrice: this.priceMax(),
      sortBy: this.sortBy() || undefined,
      page: this.currentPage(),
      pageSize: PAGE_SIZE,
    };

    this.hasActiveFilters.set(
      query.brands!.length > 0 ||
        query.sellers!.length > 0 ||
        query.categories!.length > 0 ||
        this.priceMin() !== null ||
        this.priceMax() !== null ||
        !!query.search,
    );

    const request$ =
      this.viewMode() === 'deals'
        ? this.dealsService.getDeals(query)
        : this.viewMode() === 'store'
          ? this.dealsService.getStoreDeals(query)
          : this.dealsService.getAllProducts(query);

    request$.subscribe({
      next: (result) => {
        this.deals.set(result.items);
        this.totalCount.set(result.totalCount);
        this.totalPages.set(result.totalPages);
        this.loading.set(false);
        this.loadSparklines(result.items);
      },
      error: () => {
        this.error.set("We couldn't load the products. Please try again in a moment.");
        this.loading.set(false);
      },
    });
  }

  private loadSparklines(deals: Deal[]): void {
    this.sparklines.set(new Map());
    const ids = deals.map((d) => d.productId);
    this.dealsService.getSparklines(ids).subscribe((result) => {
      this.sparklines.set(new Map(result.map((s) => [s.productId, s.points])));
    });
  }

  protected sparklineFor(productId: number): PricePoint[] {
    return this.sparklines().get(productId) ?? [];
  }

  protected discountBadge(deal: Deal): string {
    return `-${deal.discountPercent}%`;
  }

  protected storeDiscountBadge(deal: Deal): string {
    return `Store sale -${deal.storeDiscountPercent}%`;
  }

  // Hero card: the real discount when there is one, otherwise the store sale.
  protected heroBadgeText(deal: Deal): string {
    return deal.discountPercent > 0 ? this.discountBadge(deal) : this.storeDiscountBadge(deal);
  }

  protected goToStoreUrl(deal: Deal): string {
    return this.priceHistoryService.goToStoreUrl(deal.productId, deal.storeUrl);
  }

  /** Counts the store click; the link goes straight to the store, so /go/{id}
   *  can no longer count it (see PriceHistoryService). */
  protected trackStoreClick(productId: number): void {
    this.priceHistoryService.trackStoreClick(productId);
  }

  private loadHeroDeal(): void {
    this.dealsService.getDeals({ pageSize: 1 }).subscribe({
      next: (result) => {
        if (result.items.length > 0) {
          this.setHeroDeal(result.items[0]);
          return;
        }
        // No real discount yet (common while the price history is young):
        // fall back to the biggest store sale.
        this.dealsService.getStoreDeals({ pageSize: 1 }).subscribe((storeResult) => {
          if (storeResult.items.length > 0) this.setHeroDeal(storeResult.items[0]);
        });
      },
    });
  }

  private setHeroDeal(deal: Deal): void {
    this.heroDeal.set(deal);
    this.priceHistoryService.get(deal.productId, 30).subscribe((history) => this.heroPoints.set(history.points));
  }

  protected onAlertSubmit(): void {
    const value = this.alertEmail().trim();
    if (!value) return;

    this.alertSubmitting.set(true);
    this.subscribeService.subscribe(value).subscribe({
      next: (result) => {
        this.alertStatusMessage.set(result.message);
        this.alertEmail.set('');
        this.alertSubmitting.set(false);
      },
      error: () => {
        this.alertStatusMessage.set('Something went wrong. Please try again in a moment.');
        this.alertSubmitting.set(false);
      },
    });
  }

  protected lastCheckedText(deal: Deal): string {
    return formatRelativeTime(deal.scrapedAt);
  }

  // The label from CATEGORY_LABELS (shared with the footer and category
  // pages); an unexpected slug falls back to dashes turned into spaces.
  protected categoryLabel(category: string): string {
    return (
      CATEGORY_LABELS[category] ??
      category
        .split('-')
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
        .join(' ')
    );
  }

  protected categoryIconPath(category: string): string {
    return CATEGORY_ICON_PATHS[category] ?? DEFAULT_CATEGORY_ICON;
  }

  protected categoryPhosphorIcon(category: string): string {
    return categoryPhosphorIcon(category);
  }

  protected setTheme(preference: ThemePreference): void {
    this.theme.setPreference(preference);
  }

  protected toggleCategories(): void {
    this.categoriesOpen.update((open) => !open);
  }

  protected closeCategories(): void {
    this.categoriesOpen.set(false);
  }

  // Card links are RouterLinks (see core/product-link.ts): a real <a href>
  // that crawlers follow and middle-click opens in a new tab, while a normal
  // click still navigates inside the SPA, exactly like openDeal.
  protected productPath(deal: Deal): string {
    return productPath(deal);
  }

  protected openDeal(deal: Deal): void {
    this.router.navigate(['/product', deal.productId, slugify(deal.productName)], { queryParamsHandling: 'preserve' });
  }

  /**
   * Closes the modal.
   *
   * Arrived from inside the app: go BACK, do NOT navigate to '/'. A new
   * navigation added a THIRD history entry ([/] -> [/product/123] -> [/]) and
   * the phone's back button then reopened the modal just closed.
   *
   * Arrived directly on /product/:id (search result, shared link): there is
   * no page of this site behind it, and location.back() would leave the site,
   * so navigate home instead. The component is created DURING the first
   * navigation and misses its NavigationEnd, so a count of 0 means the visitor
   * came in directly.
   */
  protected closeDeal(): void {
    if (this.inAppNavigations > 0) {
      this.location.back();
      return;
    }

    this.router.navigate(['/'], { queryParamsHandling: 'preserve' });
  }

  // Only when the product states its serving size and the package is sold by
  // weight; counts such as capsules would need a guess, so they show nothing.
  protected perServing(deal: Deal): number | null {
    return pricePerServing(deal);
  }

  @HostListener('document:keydown', ['$event'])
  protected onGlobalKeydown(event: KeyboardEvent): void {
    const isShortcut = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k';
    if (!isShortcut) return;

    event.preventDefault();
    this.searchInput()?.nativeElement.focus();
  }

  @HostListener('window:scroll')
  protected onWindowScroll(): void {
    this.showScrollTop.set(window.scrollY > 400);
  }

  protected scrollToTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
