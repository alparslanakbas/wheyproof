import { isPlatformBrowser } from '@angular/common';
import { Component, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { MARKET, formatPrice } from '../core/market';
import { PageMetaService } from '../core/page-meta.service';
import { normalizeSearchText } from '../core/search-normalize';
import { SITE_NAME } from '../core/site-identity';
import {
  AdminBrand,
  AdminFailure,
  AdminProduct,
  AdminService,
  AdminStatus,
  Coupon,
  SecurityEventsResponse,
} from './admin.service';

type Tab = 'status' | 'events' | 'coupons' | 'visibility';
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
  imports: [FormsModule],
  templateUrl: './admin-page.html',
  styleUrls: ['./admin-page.css', './admin-page-support.css'],
})
export class AdminPage implements OnInit {
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
  readonly productSearchDone = signal(false);
  readonly visibilityMessage = signal<string | null>(null);
  readonly visibilityUpdatedAt = signal<Date | null>(null);
  readonly pendingChange = signal<PendingVisibilityChange | null>(null);
  readonly changeInProgress = signal(false);

  readonly filteredBrands = computed(() => {
    const query = normalizeSearchText(this.brandSearch());
    const filter = this.brandFilter();
    return this.brands().filter((brand) => {
      const matchesSearch = !query || normalizeSearchText(brand.name).includes(query);
      const matchesFilter = filter === 'all' || (filter === 'visible' ? brand.isActive : !brand.isActive);
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

  readonly activeBrandCount = computed(() => this.brands().filter((brand) => brand.isActive).length);
  readonly hiddenBrandCount = computed(() => this.brands().filter((brand) => !brand.isActive).length);
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
          e?.status === 429 ? 'Too many attempts. Try again in 15 minutes.' : "The key wasn't accepted.",
        );
      },
    });
  }

  signOut(): void {
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
        this.brandsLoadedOnce = false;
      },
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
    if (code === 502 || code === 503 || code === 504) return 'The server is updating. Try again in a few seconds.';

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
          this.newCoupon.set({ targetType: 'brand', target: '', code: '', description: '', validUntil: '' });
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

  searchProducts(): void {
    const query = this.productSearch().trim();
    if (!query && !this.hiddenOnly()) {
      this.products.set([]);
      this.productSearchDone.set(false);
      return;
    }

    this.productsLoading.set(true);
    this.productSearchDone.set(true);
    this.visibilityMessage.set(null);
    this.api.products(query, this.hiddenOnly()).subscribe({
      next: (products) => {
        this.products.set(products);
        this.productsLoading.set(false);
        this.visibilityUpdatedAt.set(new Date());
      },
      error: () => {
        this.productsLoading.set(false);
        this.visibilityMessage.set("Couldn't search products.");
      },
    });
  }

  onHiddenOnlyChange(value: boolean): void {
    this.hiddenOnly.set(value);
    this.searchProducts();
  }

  // Hiding asks for confirmation; publishing again applies right away.
  requestVisibilityChange(type: 'brand' | 'product', id: number, name: string, currentlyActive: boolean): void {
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
        if (change.type === 'product') this.searchProducts();
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
