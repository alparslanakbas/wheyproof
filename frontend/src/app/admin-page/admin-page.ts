import { isPlatformBrowser } from '@angular/common';
import { Component, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminEditorFocus } from './admin-editor-focus';
import { AdminFailureReason } from './admin-failure-reason';
import { Observable } from 'rxjs';

import { CATEGORY_LABELS } from '../core/category-labels';
import { MARKET, formatPrice } from '../core/market';
import { PageMetaService } from '../core/page-meta.service';
import { normalizeSearchText } from '../core/search-normalize';
import { SITE_NAME } from '../core/site-identity';
import {
  NUTRITION_UNITS,
  NutritionRowForm,
  ROW_NAME_SUGGESTIONS,
  ROW_TEMPLATES,
  storedOtherRows,
  templateRowsToAdd,
} from './nutrition-rows';
import { sitePath } from '../core/site-path';
import {
  AdminBrand,
  AdminFailure,
  AdminProduct,
  AdminService,
  AdminStatus,
  AdminSubscriber,
  Coupon,
  SecurityEventsResponse,
  SubscriberStatus,
  SubscribersResponse,
} from './admin.service';

type Tab = 'status' | 'events' | 'coupons' | 'visibility' | 'subscribers';
type NutritionField = 'servingSizeGrams' | 'calories' | 'protein' | 'carbs' | 'fat' | 'fiber';

/** The admin editor's working copy; inputs are kept as text until saved. */
interface ProductDataForm extends Record<NutritionField, string> {
  product: AdminProduct;
  /** '' = automatic. */
  category: string;
  /** Label rows beyond the macros (Supplement Facts). */
  otherRows: NutritionRowForm[];
}
type SubscriberFilter = 'all' | SubscriberStatus;
type VisibilityView = 'brands' | 'products';
type BrandFilter = 'all' | 'visible' | 'hidden';

interface PendingVisibilityChange {
  type: 'brand' | 'product';
  id: number;
  name: string;
}

const BRAND_PAGE_SIZE = 5;

/**
 * Admin panel.
 *
 * Cloudflare Access outside and the HttpOnly admin session inside are two
 * separate security layers. The admin key is never written to any browser
 * storage. This route renders on the client only (see app.routes.server.ts).
 */
@Component({
  selector: 'app-admin-page',
  imports: [FormsModule, AdminEditorFocus, AdminFailureReason],
  templateUrl: './admin-page.html',
  styleUrls: [
    './admin-page.css',
    './admin-page-support.css',
    './admin-page-workspace.css',
    './admin-page-data.css',
  ],
})
export class AdminPage implements OnInit {
  // Same origin as the panel, under the edition's path (/uk/admin/api in the UK section).
  protected readonly digestPreviewUrl = sitePath('/admin/api/digest/preview');

  private readonly api = inject(AdminService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private brandsLoadedOnce = false;

  readonly signedIn = signal(false);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly tab = signal<Tab>('status');

  readonly key = signal('');
  readonly signInError = signal<string | null>(null);
  readonly signingIn = signal(false);

  readonly status = signal<AdminStatus | null>(null);
  readonly events = signal<SecurityEventsResponse | null>(null);
  readonly adminFailures = signal<AdminFailure[]>([]);
  readonly coupons = signal<Coupon[]>([]);

  readonly eventDays = signal(7);
  readonly eventKind = signal<string | null>(null);
  readonly filteredIp = signal<string | null>(null);

  readonly newCoupon = signal({
    targetType: 'brand' as 'brand' | 'seller',
    target: '',
    code: '',
    description: '',
    validUntil: '',
  });
  readonly couponMessage = signal<string | null>(null);

  /** The coupon being edited inline; null when no edit is open. */
  readonly editingCoupon = signal<{
    id: number;
    code: string;
    description: string;
    validUntil: string;
  } | null>(null);
  readonly couponSaving = signal(false);

  readonly visibilityView = signal<VisibilityView>('brands');
  readonly brands = signal<AdminBrand[]>([]);
  readonly brandsLoading = signal(false);
  readonly brandSearch = signal('');
  readonly brandFilter = signal<BrandFilter>('all');
  readonly brandPage = signal(1);
  readonly products = signal<AdminProduct[]>([]);
  readonly productsLoading = signal(false);
  readonly productSearch = signal('');
  readonly hiddenOnly = signal(false);
  readonly missingNutritionOnly = signal(false);
  readonly uncategorisedOnly = signal(false);
  /** Rows no automatic source can still fill: the ones worth typing by hand. */
  readonly needsManualOnly = signal(false);
  /** Server-side paging: the filters match thousands of rows. */
  readonly productPage = signal(1);
  readonly productTotal = signal(0);
  readonly productPageSize = signal(50);
  readonly productPageCount = computed(() =>
    Math.max(1, Math.ceil(this.productTotal() / Math.max(1, this.productPageSize()))),
  );
  readonly visibleProductPages = computed(() => {
    const total = this.productPageCount();
    const start = Math.min(Math.max(1, this.productPage() - 2), Math.max(1, total - 4));
    return Array.from({ length: Math.min(5, total) }, (_, index) => start + index);
  });
  readonly productRangeStart = computed(
    () => (this.productPage() - 1) * this.productPageSize() + 1,
  );
  readonly productRangeEnd = computed(() =>
    Math.min(this.productRangeStart() + this.products().length - 1, this.productTotal()),
  );

  readonly categoryOptions = Object.entries(CATEGORY_LABELS).map(([slug, label]) => ({
    slug,
    label,
  }));
  readonly nutritionFields: { key: NutritionField; label: string }[] = [
    { key: 'servingSizeGrams', label: 'Serving size (g)' },
    { key: 'calories', label: 'Calories' },
    { key: 'protein', label: 'Protein (g)' },
    { key: 'carbs', label: 'Total carbohydrate (g)' },
    { key: 'fat', label: 'Total fat (g)' },
    { key: 'fiber', label: 'Dietary fiber (g)' },
  ];
  readonly nutritionUnits = NUTRITION_UNITS;
  readonly rowNameSuggestions = ROW_NAME_SUGGESTIONS;
  /** The product whose category and nutrition are being edited; null when closed. */
  readonly editingData = signal<ProductDataForm | null>(null);
  readonly dataSaving = signal(false);
  readonly dataMessage = signal<string | null>(null);
  readonly productSearchDone = signal(false);
  readonly visibilityMessage = signal<string | null>(null);
  readonly visibilityUpdatedAt = signal<Date | null>(null);
  readonly pendingChange = signal<PendingVisibilityChange | null>(null);
  readonly changeInProgress = signal(false);

  readonly subscriberData = signal<SubscribersResponse | null>(null);
  readonly subscribersLoading = signal(false);
  readonly subscriberSearch = signal('');
  readonly subscriberFilter = signal<SubscriberFilter>('all');
  readonly subscriberMessage = signal<string | null>(null);
  readonly pendingDeactivation = signal<AdminSubscriber | null>(null);
  /** Id of the row whose request is running, so only that row's buttons disable. */
  readonly subscriberBusyId = signal<number | null>(null);

  readonly filteredSubscribers = computed(() => {
    const query = this.subscriberSearch().trim().toLowerCase();
    const filter = this.subscriberFilter();
    return (this.subscriberData()?.subscribers ?? []).filter(
      (s) => (filter === 'all' || s.status === filter) && (!query || s.email.includes(query)),
    );
  });

  readonly filteredBrands = computed(() => {
    const query = normalizeSearchText(this.brandSearch());
    const filter = this.brandFilter();
    return this.brands().filter((brand) => {
      const matchesSearch = !query || normalizeSearchText(brand.name).includes(query);
      const matchesFilter =
        filter === 'all' || (filter === 'visible' ? brand.isActive : !brand.isActive);
      return matchesSearch && matchesFilter;
    });
  });

  readonly brandPageCount = computed(() =>
    Math.max(1, Math.ceil(this.filteredBrands().length / BRAND_PAGE_SIZE)),
  );

  readonly pagedBrands = computed(() => {
    const start = (this.brandPage() - 1) * BRAND_PAGE_SIZE;
    return this.filteredBrands().slice(start, start + BRAND_PAGE_SIZE);
  });

  readonly visibleBrandPages = computed(() => {
    const total = this.brandPageCount();
    const current = this.brandPage();
    const start = Math.min(Math.max(1, current - 2), Math.max(1, total - 4));
    return Array.from({ length: Math.min(5, total) }, (_, index) => start + index);
  });

  readonly brandRangeStart = computed(() => (this.brandPage() - 1) * BRAND_PAGE_SIZE + 1);
  readonly brandRangeEnd = computed(() =>
    Math.min(this.brandPage() * BRAND_PAGE_SIZE, this.filteredBrands().length),
  );

  readonly activeBrandCount = computed(
    () => this.brands().filter((brand) => brand.isActive).length,
  );
  readonly hiddenBrandCount = computed(
    () => this.brands().filter((brand) => !brand.isActive).length,
  );
  readonly visibilityProductTotal = computed(
    () =>
      this.status()?.products.total ??
      this.brands().reduce((total, brand) => total + brand.productCount, 0),
  );

  ngOnInit(): void {
    this.pageMeta.set({
      title: 'Admin | ' + SITE_NAME,
      description: 'Admin panel.',
      canonicalPath: '/',
      noIndex: true,
    });

    if (!this.isBrowser) return;

    this.api.status().subscribe({
      next: (s) => {
        this.status.set(s);
        this.signedIn.set(true);
        this.loading.set(false);
        this.loadEvents();
        this.loadCoupons();
      },
      error: () => {
        this.signedIn.set(false);
        this.loading.set(false);
      },
    });
  }

  signIn(): void {
    const key = this.key().trim();
    if (!key) return;

    this.signingIn.set(true);
    this.signInError.set(null);

    this.api.signIn(key).subscribe({
      next: () => {
        this.key.set('');
        this.signingIn.set(false);
        this.signedIn.set(true);
        this.loadStatus();
        this.loadEvents();
        this.loadCoupons();
      },
      error: (e) => {
        this.signingIn.set(false);
        this.signInError.set(
          e?.status === 429
            ? 'Too many attempts. Try again in 15 minutes.'
            : "The key wasn't accepted.",
        );
      },
    });
  }

  /**
   * Ends BOTH sessions. Deleting our cookie alone isn't a sign-out: the
   * Cloudflare Access session stays, the next reload sends its token again and
   * the panel opens without asking. /cdn-cgi/access/logout on this host clears
   * the Access cookie, so the next visit goes back through the Access login.
   * The redirect runs even if our DELETE fails; the Access session is the one
   * that actually lets the browser back in.
   */
  signOut(): void {
    const leaveAccess = () => window.location.assign('/cdn-cgi/access/logout');
    this.api.signOut().subscribe({
      next: () => {
        this.signedIn.set(false);
        this.status.set(null);
        this.events.set(null);
        this.adminFailures.set([]);
        this.coupons.set([]);
        this.editingCoupon.set(null);
        this.brands.set([]);
        this.products.set([]);
        this.subscriberData.set(null);
        this.brandsLoadedOnce = false;
        leaveAccess();
      },
      error: leaveAccess,
    });
  }

  selectTab(tab: Tab): void {
    this.tab.set(tab);
    this.error.set(null);
    // The coupons tab needs the brand list too: brand names were typed by
    // hand and had to match the catalog's spelling exactly. The suggestion
    // list removes the guesswork.
    const needsBrands = tab === 'visibility' || tab === 'coupons';
    if (needsBrands && !this.brandsLoadedOnce) {
      this.loadBrands(false);
    }
    // Loaded on every visit, not once: a subscription can be confirmed from
    // an inbox while the panel is open.
    if (tab === 'subscribers') this.loadSubscribers();
  }

  loadSubscribers(): void {
    this.subscribersLoading.set(true);
    this.api.subscribers().subscribe({
      next: (data) => {
        this.subscriberData.set(data);
        this.subscribersLoading.set(false);
      },
      error: (e) => {
        this.subscribersLoading.set(false);
        this.subscriberMessage.set(this.errorText(e, "Couldn't load subscribers."));
      },
    });
  }

  /** Deactivating stops someone's email; it asks first, like hiding a brand. */
  requestDeactivation(subscriber: AdminSubscriber): void {
    this.subscriberMessage.set(null);
    this.pendingDeactivation.set(subscriber);
  }

  cancelDeactivation(): void {
    if (this.subscriberBusyId() !== null) return;
    this.pendingDeactivation.set(null);
  }

  confirmDeactivation(): void {
    const subscriber = this.pendingDeactivation();
    if (!subscriber) return;

    this.subscriberBusyId.set(subscriber.id);
    this.api.deactivateSubscriber(subscriber.id).subscribe({
      next: () => {
        this.subscriberBusyId.set(null);
        this.pendingDeactivation.set(null);
        this.subscriberMessage.set(`${subscriber.email} is no longer subscribed.`);
        this.loadSubscribers();
      },
      error: (e) => {
        this.subscriberBusyId.set(null);
        this.pendingDeactivation.set(null);
        this.subscriberMessage.set(this.errorText(e, "Couldn't deactivate the subscriber."));
      },
    });
  }

  sendConfirmation(subscriber: AdminSubscriber): void {
    this.subscriberMessage.set(null);
    this.subscriberBusyId.set(subscriber.id);
    this.api.sendSubscriberConfirmation(subscriber.id).subscribe({
      next: () => {
        this.subscriberBusyId.set(null);
        this.subscriberMessage.set(`Confirmation email sent to ${subscriber.email}.`);
        this.loadSubscribers();
      },
      error: (e) => {
        this.subscriberBusyId.set(null);
        // The backend explains the cooldown and provider failures in its own
        // words; a generic "failed" would hide which one happened.
        const body = (e as { error?: unknown } | null)?.error;
        const message =
          typeof body === 'string' ? body : (body as { message?: string } | null)?.message;
        this.subscriberMessage.set(
          message?.trim() || this.errorText(e, "Couldn't send the confirmation email."),
        );
      },
    });
  }

  selectSubscriberFilter(filter: SubscriberFilter): void {
    this.subscriberFilter.set(filter);
  }

  subscriberStatusLabel(status: SubscriberStatus): string {
    switch (status) {
      case 'active':
        return 'Active';
      case 'pending':
        return 'Awaiting confirmation';
      default:
        return 'Unsubscribed';
    }
  }

  loadStatus(): void {
    this.api.status().subscribe({
      next: (s) => this.status.set(s),
      error: (e) => this.error.set(this.errorText(e, "Couldn't load the status.")),
    });
  }

  loadEvents(): void {
    this.filteredIp.set(null);
    this.api.securityEvents(this.eventDays(), this.eventKind()).subscribe({
      next: (e) => this.events.set(e),
      error: (e) => this.error.set(this.errorText(e, "Couldn't load events.")),
    });
    this.loadAdminFailures();
  }

  /**
   * Shares the day range with the event list but ignores the KIND filter:
   * that filter is for security event kinds ("probe" and so on) and has no
   * meaning for admin failures.
   */
  private loadAdminFailures(): void {
    this.api.adminFailures(this.eventDays()).subscribe({
      next: (f) => this.adminFailures.set(f),
      error: (e) => this.error.set(this.errorText(e, "Couldn't load admin failures.")),
    });
  }

  loadCoupons(): void {
    this.api.coupons().subscribe({
      next: (c) => this.coupons.set(c),
      error: (e) => this.error.set(this.errorText(e, "Couldn't load coupons.")),
    });
  }

  selectEventKind(kind: string | null): void {
    this.eventKind.set(kind);
    this.loadEvents();
  }

  selectEventDays(days: number): void {
    this.eventDays.set(days);
    this.loadEvents();
  }

  filterByIp(ip: string): void {
    this.tab.set('events');
    this.api.securityEvents(90, null).subscribe({
      next: (e) => {
        this.events.set({ ...e, events: e.events.filter((event) => event.ip === ip) });
        this.filteredIp.set(ip);
      },
    });
  }

  startCouponEdit(coupon: Coupon): void {
    this.couponMessage.set(null);
    this.editingCoupon.set({
      id: coupon.id,
      code: coupon.code ?? '',
      description: coupon.description,
      // <input type="date"> only accepts YYYY-MM-DD; the API returns a full
      // timestamp, so it's trimmed.
      validUntil: coupon.validUntil ? coupon.validUntil.slice(0, 10) : '',
    });
  }

  cancelCouponEdit(): void {
    if (this.couponSaving()) return;
    this.editingCoupon.set(null);
  }

  updateCouponField(field: 'code' | 'description' | 'validUntil', value: string): void {
    const current = this.editingCoupon();
    if (!current) return;
    this.editingCoupon.set({ ...current, [field]: value });
  }

  saveCouponEdit(): void {
    const edit = this.editingCoupon();
    if (!edit) return;

    if (!edit.description.trim()) {
      this.couponMessage.set("The description can't be empty.");
      return;
    }

    this.couponSaving.set(true);
    this.api
      .updateCoupon(edit.id, {
        // An EMPTY STRING is sent, NOT null. On this endpoint null means
        // "leave this field alone"; if emptying the field sent null, the code
        // would stay and "I saved but nothing changed" would follow. An empty
        // string really removes the code on the backend.
        code: edit.code.trim(),
        description: edit.description.trim(),
        validUntil: edit.validUntil || null,
        // A date has no empty string, so clearing it needs an explicit flag.
        clearValidUntil: edit.validUntil ? undefined : true,
        // isActive is DELIBERATELY not sent: fixing the text mustn't change
        // whether the coupon is published.
      })
      .subscribe({
        next: () => {
          this.couponSaving.set(false);
          this.editingCoupon.set(null);
          this.couponMessage.set('Coupon updated.');
          this.loadCoupons();
        },
        error: (e) => {
          this.couponSaving.set(false);
          this.couponMessage.set(this.errorText(e, "Couldn't update the coupon."));
        },
      });
  }

  /**
   * Coupon creation errors: THE BACKEND'S OWN MESSAGE COMES FIRST.
   *
   * Showing a generic "Couldn't add the coupon." for every failure hid the
   * real reason: a brand name typed slightly differently from the catalog
   * got a 404 with exactly "No brand named 'X'.", and the panel threw that
   * sentence away. The cause was on the wire and we hid it.
   */
  private couponCreateError(e: unknown): string {
    const response = e as { status?: number; error?: unknown } | null;

    // The backend may return plain text or { message }; accept both.
    const body = response?.error;
    const message =
      typeof body === 'string' ? body : ((body as { message?: string } | null)?.message ?? null);

    if (message && message.trim().length > 0) return message.trim();
    if (response?.status === 400) return 'Set exactly one of brand or seller.';
    return this.errorText(e, "Couldn't add the coupon.");
  }

  /**
   * Turns a status code into a sentence a person understands.
   *
   * WHY: a panel that says "500" for every failure makes a 502 during a
   * deploy (the few seconds the backend restarts) look like a lasting
   * failure. Mistaking a temporary problem for a permanent one sends you
   * hunting for a bug that isn't there.
   */
  private errorText(e: unknown, fallback: string): string {
    const code = (e as { status?: number } | null)?.status;

    if (code === 0) return "Couldn't reach the server. Check your connection.";
    if (code === 401) return 'Your session ended. Reload the page and sign in again.';
    if (code === 429) return 'Too many requests; wait a moment.';
    if (code === 502 || code === 503 || code === 504)
      return 'The server is updating. Try again in a few seconds.';

    return code ? `${fallback} (code ${code})` : fallback;
  }

  toggleCouponActive(coupon: Coupon): void {
    this.api
      .updateCoupon(coupon.id, {
        code: coupon.code,
        description: coupon.description,
        validUntil: coupon.validUntil,
        isActive: !coupon.isActive,
      })
      .subscribe({
        next: () => this.loadCoupons(),
        error: (e) => this.couponMessage.set(this.errorText(e, "Couldn't update the coupon.")),
      });
  }

  createCoupon(): void {
    const form = this.newCoupon();
    if (!form.target.trim() || !form.description.trim()) {
      this.couponMessage.set('Brand/seller and description are required.');
      return;
    }

    this.api
      .createCoupon({
        brandName: form.targetType === 'brand' ? form.target.trim() : null,
        seller: form.targetType === 'seller' ? form.target.trim() : null,
        code: form.code.trim() || null,
        description: form.description.trim(),
        validUntil: form.validUntil || null,
      })
      .subscribe({
        next: () => {
          this.couponMessage.set('Coupon added.');
          this.newCoupon.set({
            targetType: 'brand',
            target: '',
            code: '',
            description: '',
            validUntil: '',
          });
          this.loadCoupons();
        },
        error: (e) => this.couponMessage.set(this.couponCreateError(e)),
      });
  }

  loadBrands(clearMessage = true): void {
    this.brandsLoading.set(true);
    if (clearMessage) this.visibilityMessage.set(null);
    this.api.brands().subscribe({
      next: (brands) => {
        this.brands.set(brands);
        this.brandsLoadedOnce = true;
        this.brandsLoading.set(false);
        this.visibilityUpdatedAt.set(new Date());
        this.clampBrandPage();
      },
      error: () => {
        this.brandsLoading.set(false);
        this.visibilityMessage.set("Couldn't load brand visibility.");
      },
    });
  }

  selectVisibilityView(view: VisibilityView): void {
    this.visibilityView.set(view);
    this.visibilityMessage.set(null);
  }

  onBrandSearch(value: string): void {
    this.brandSearch.set(value);
    this.brandPage.set(1);
  }

  selectBrandFilter(filter: BrandFilter): void {
    this.brandFilter.set(filter);
    this.brandPage.set(1);
  }

  goToBrandPage(page: number): void {
    this.brandPage.set(Math.min(Math.max(1, page), this.brandPageCount()));
  }

  /** A new search or filter starts at page 1; a refresh after an edit passes the current page. */
  searchProducts(page = 1): void {
    const query = this.productSearch().trim();
    const anyFilter =
      this.hiddenOnly() || this.missingNutritionOnly() || this.uncategorisedOnly() || this.needsManualOnly();
    if (!query && !anyFilter) {
      this.products.set([]);
      this.productTotal.set(0);
      this.productPage.set(1);
      this.productSearchDone.set(false);
      return;
    }

    this.productsLoading.set(true);
    this.productSearchDone.set(true);
    this.visibilityMessage.set(null);
    this.api
      .products(
        query,
        this.hiddenOnly(),
        this.missingNutritionOnly(),
        this.uncategorisedOnly(),
        this.needsManualOnly(),
        page,
      )
      .subscribe({
        next: (result) => {
          const lastPage = Math.max(1, Math.ceil(result.total / Math.max(1, result.pageSize)));
          // Saving nutrition under "missing nutrition" takes rows out of the list;
          // the page being viewed can end up past the last one.
          if (result.items.length === 0 && result.total > 0 && page > lastPage) {
            this.searchProducts(lastPage);
            return;
          }
          this.products.set(result.items);
          this.productTotal.set(result.total);
          this.productPage.set(result.page);
          this.productPageSize.set(result.pageSize);
          this.productsLoading.set(false);
          this.visibilityUpdatedAt.set(new Date());
        },
        error: () => {
          this.productsLoading.set(false);
          this.visibilityMessage.set("Couldn't search products.");
        },
      });
  }

  goToProductPage(page: number): void {
    this.searchProducts(Math.min(Math.max(1, page), this.productPageCount()));
  }

  onHiddenOnlyChange(value: boolean): void {
    this.hiddenOnly.set(value);
    this.searchProducts();
  }

  onDataFilterChange(filter: 'missingNutrition' | 'uncategorised' | 'needsManual', value: boolean): void {
    const signals = {
      missingNutrition: this.missingNutritionOnly,
      uncategorised: this.uncategorisedOnly,
      needsManual: this.needsManualOnly,
    };
    signals[filter].set(value);
    this.searchProducts();
  }

  categoryLabel(slug: string | null): string {
    return slug ? (CATEGORY_LABELS[slug] ?? slug) : '—';
  }

  openDataEditor(product: AdminProduct): void {
    const table = this.parseNutrition(product.nutritionJson);
    // "160", "2.5g" -> the number as text for the input.
    const value = (label: string) => table[label]?.match(/\d+(?:\.\d+)?/)?.[0] ?? '';
    const other = storedOtherRows(table);
    // An automatic reading can hold rows this editor can't express ("10%").
    // Saving replaces the whole table, so say which ones would go.
    this.dataMessage.set(
      other.skipped.length > 0
        ? `Saving from here drops rows this editor can't edit: ${other.skipped.join(', ')}.`
        : null,
    );
    this.editingData.set({
      product,
      category: product.categoryIsManual ? (product.category ?? '') : '',
      servingSizeGrams: product.servingSizeGrams?.toString() ?? value('Serving Size'),
      calories: value('Calories'),
      protein: value('Protein'),
      carbs: value('Total Carbohydrate'),
      fat: value('Total Fat'),
      fiber: value('Dietary Fiber'),
      otherRows: other.rows,
    });
  }

  /** The label of the category whose template rows can still be added, or null. */
  templateCategoryLabel(form: ProductDataForm): string | null {
    const category = form.category || form.product.category;
    if (!category || !ROW_TEMPLATES[category]) return null;
    return templateRowsToAdd(category, form.otherRows).length > 0
      ? this.categoryLabel(category)
      : null;
  }

  addTemplateRows(): void {
    const form = this.editingData();
    if (!form) return;
    const rows = templateRowsToAdd(form.category || form.product.category, form.otherRows);
    this.editingData.set({ ...form, otherRows: [...form.otherRows, ...rows] });
  }

  addOtherRow(): void {
    const form = this.editingData();
    if (!form) return;
    this.editingData.set({
      ...form,
      otherRows: [...form.otherRows, { label: '', amount: '', unit: 'mg' }],
    });
  }

  removeOtherRow(index: number): void {
    const form = this.editingData();
    if (!form) return;
    this.editingData.set({ ...form, otherRows: form.otherRows.filter((_, i) => i !== index) });
  }

  updateOtherRow(index: number, field: keyof NutritionRowForm, value: unknown): void {
    const form = this.editingData();
    if (!form) return;
    const text = value == null ? '' : String(value);
    this.editingData.set({
      ...form,
      otherRows: form.otherRows.map((row, i) => (i === index ? { ...row, [field]: text } : row)),
    });
  }

  closeDataEditor(): void {
    if (this.dataSaving()) return;
    this.editingData.set(null);
  }

  updateDataField(field: NutritionField | 'category', value: unknown): void {
    const form = this.editingData();
    if (!form) return;
    this.editingData.set({ ...form, [field]: value == null ? '' : String(value) });
  }

  saveCategory(): void {
    const form = this.editingData();
    if (!form) return;
    this.runDataEdit(
      this.api.setProductCategory(form.product.id, form.category || null),
      'Category saved',
    );
  }

  saveNutrition(): void {
    const form = this.editingData();
    if (!form) return;

    // Empty stays null (not 0): a blank fiber means "not entered", and the
    // backend requires the four core values itself.
    const number = (text: string) => (text.trim() === '' ? null : Number(text));
    const body = {
      servingSizeGrams: number(form.servingSizeGrams),
      calories: number(form.calories),
      proteinGrams: number(form.protein),
      carbohydrateGrams: number(form.carbs),
      fatGrams: number(form.fat),
      fiberGrams: number(form.fiber),
    };
    // A template row left without an amount wasn't on the label: skipped, not
    // sent as an error. A named row with an amount goes to the backend's check.
    const otherRows = form.otherRows
      .filter((r) => r.amount.trim() !== '')
      .map((r) => ({ label: r.label, amount: number(r.amount), unit: r.unit }));
    if (
      Object.values(body).some((v) => v !== null && Number.isNaN(v)) ||
      otherRows.some((r) => r.amount !== null && Number.isNaN(r.amount))
    ) {
      this.dataMessage.set('Enter numbers only.');
      return;
    }

    this.runDataEdit(
      this.api.setProductNutrition(form.product.id, { ...body, otherRows }),
      'Nutrition saved',
    );
  }

  clearNutrition(): void {
    const form = this.editingData();
    if (!form) return;
    this.runDataEdit(this.api.clearProductNutrition(form.product.id), 'Nutrition cleared');
  }

  private runDataEdit(request: Observable<{ rowsUpdated: number }>, done: string): void {
    this.dataSaving.set(true);
    this.dataMessage.set(null);
    request.subscribe({
      next: (r) => {
        this.dataSaving.set(false);
        this.dataMessage.set(
          `${done} for ${r.rowsUpdated} row${r.rowsUpdated === 1 ? '' : 's'} (every size of this page).`,
        );
        this.searchProducts(this.productPage());
      },
      error: (e) => {
        this.dataSaving.set(false);
        // A refused value comes back with its reason ("calories 400 don't match
        // the macros"); a generic "failed" would hide what to fix.
        const body = (e as { error?: unknown } | null)?.error;
        const message =
          typeof body === 'string' ? body : (body as { message?: string } | null)?.message;
        this.dataMessage.set(message?.trim() || this.errorText(e, "Couldn't save."));
      },
    });
  }

  private parseNutrition(json: string | null): Record<string, string> {
    if (!json) return {};
    try {
      return JSON.parse(json) as Record<string, string>;
    } catch {
      return {};
    }
  }

  // Hiding asks for confirmation; publishing again applies right away.
  requestVisibilityChange(
    type: 'brand' | 'product',
    id: number,
    name: string,
    currentlyActive: boolean,
  ): void {
    if (currentlyActive) {
      this.pendingChange.set({ type, id, name });
      return;
    }

    this.applyVisibilityChange({ type, id, name }, true);
  }

  confirmChange(): void {
    const change = this.pendingChange();
    if (!change) return;
    this.applyVisibilityChange(change, false);
  }

  cancelChange(): void {
    if (this.changeInProgress()) return;
    this.pendingChange.set(null);
  }

  date(value: string | null | undefined): string {
    if (!value) return '—';
    return new Date(value).toLocaleString(MARKET.locale, {
      dateStyle: 'short',
      timeStyle: 'short',
      timeZone: MARKET.timeZone,
    });
  }

  price(value: number | null | undefined): string {
    return value == null ? '—' : formatPrice(value);
  }

  todayLabel(): string {
    return new Intl.DateTimeFormat(MARKET.locale, {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: MARKET.timeZone,
    }).format(new Date());
  }

  hoursAgo(value: string | Date | null | undefined): string {
    if (!value) return '—';
    const hours = (Date.now() - new Date(value).getTime()) / 3600000;
    if (hours < 1) return 'just now';
    if (hours < 48) return `${Math.round(hours)} h ago`;
    return `${Math.round(hours / 24)} days ago`;
  }

  kindLabel(kind: string): string {
    switch (kind) {
      case 'unauthorized':
        return 'Unauthorized';
      case 'rate-limited':
        return 'Rate limited';
      case 'probe':
        return 'Vulnerability scan';
      case 'server-error':
        return 'Server error';
      default:
        return kind;
    }
  }

  nutritionPercent(): number {
    const s = this.status();
    if (!s || s.products.total === 0) return 0;
    return Math.round((s.products.withNutrition / s.products.total) * 1000) / 10;
  }

  private applyVisibilityChange(change: PendingVisibilityChange, isActive: boolean): void {
    this.changeInProgress.set(true);
    this.visibilityMessage.set(null);
    const request =
      change.type === 'brand'
        ? this.api.setBrandActive(change.id, isActive)
        : this.api.setProductActive(change.id, isActive);

    request.subscribe({
      next: () => {
        this.changeInProgress.set(false);
        this.pendingChange.set(null);
        this.visibilityMessage.set(`${change.name} ${isActive ? 'is live again.' : 'hidden.'}`);
        this.loadBrands(false);
        if (change.type === 'product') this.searchProducts(this.productPage());
      },
      error: () => {
        this.changeInProgress.set(false);
        this.visibilityMessage.set("Couldn't save the visibility change.");
      },
    });
  }

  private clampBrandPage(): void {
    if (this.brandPage() > this.brandPageCount()) {
      this.brandPage.set(this.brandPageCount());
    }
  }
}
