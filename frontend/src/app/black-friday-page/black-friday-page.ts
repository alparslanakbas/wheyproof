import { DOCUMENT } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { CampaignDiscount, CampaignSummary, blackFriday, cyberMonday } from '../core/campaign';
import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { CURRENT_EDITION } from '../core/editions';
import { MARKET, formatPrice } from '../core/market';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { PriceHistoryService } from '../core/price-history.service';
import { productPath } from '../core/product-link';
import { SITE_NAME } from '../core/site-identity';
import { NewsletterSignup } from '../newsletter-signup/newsletter-signup';
import { SiteHeader } from '../site-header/site-header';

/**
 * Season page (Black Friday and Cyber Monday). Mirror of the Turkish site's /efsane-kasim (2026-10-07); the address
 * has no year and stays the same every year so the page keeps its authority; the year is computed on each request.
 *
 * The discount rule is NOT the product cards' "Real discount" label: today's price against the lowest price in the
 * 30 days before the discount started (backend CampaignDiscountService). That is the EU rule for "was" prices; the
 * page presents it as our rule, not as US or UK law, and says how it differs from the label in the FAQ.
 */
@Component({
  selector: 'app-black-friday-page',
  imports: [RouterLink, NewsletterSignup, SiteHeader],
  templateUrl: './black-friday-page.html',
})
export class BlackFridayPage implements OnInit {
  private readonly dealsService = inject(DealsService);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  protected readonly year = new Date().getFullYear();
  protected readonly blackFridayDate = blackFriday(this.year);
  protected readonly cyberMondayDate = cyberMonday(this.year);
  protected readonly stores = CURRENT_EDITION.code === 'UK' ? 'UK stores' : 'US stores';

  protected readonly summary = signal<CampaignSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** Products whose price history we can judge: the ones short of data before the discount are left out. */
  protected readonly judged = computed(() => {
    const s = this.summary();
    return s ? s.real + s.notCheaper + s.permanent : 0;
  });

  protected readonly displayName = displayName;
  protected readonly productPath = productPath;
  protected readonly formatPrice = formatPrice;

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Black Friday ${this.year} Supplement Deals: Which Are Real? | ${SITE_NAME}`,
      description:
        `Black Friday and Cyber Monday ${this.year} supplement deals checked against each product's lowest price ` +
        'in the 30 days before the sale. The products that really got cheaper, on one page.',
      canonicalPath: '/black-friday',
    });
    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Black Friday', path: '/black-friday' },
      ]),
    );

    this.dealsService.getCampaignSummary().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected day(date: Date): string {
    return new Intl.DateTimeFormat(MARKET.locale, { weekday: 'long', month: 'long', day: 'numeric', timeZone: 'UTC' }).format(date);
  }

  protected measuredAt(iso: string): string {
    return new Intl.DateTimeFormat(MARKET.locale, {
      month: 'long',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      timeZone: MARKET.timeZone,
      timeZoneName: 'short',
    }).format(new Date(iso));
  }

  protected percent(value: number): string {
    return value.toLocaleString(MARKET.locale, { maximumFractionDigits: 1 });
  }

  protected goToStoreUrl(discount: CampaignDiscount): string {
    return this.priceHistoryService.goToStoreUrl(discount.product.productId, discount.product.storeUrl);
  }

  protected storeClick(productId: number): void {
    this.priceHistoryService.trackStoreClick(productId);
  }
}
