import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Component, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { showFooterBrandLinks } from './core/footer-brand-links';
import { NavigationSnapshot, routePath, shouldResetScroll } from './core/scroll-reset';
import { filter } from 'rxjs';

import { brandSlug } from './core/brand-slug';
import { canonicalOrigin } from './core/canonical-link';
import { CATEGORY_LABELS } from './core/category-labels';
import { DealsService } from './core/deals.service';
import { upsertJsonLdScript } from './core/page-meta.service';
import { FOUNDER, SITE_NAME } from './core/site-identity';
import { ComparisonBar } from './comparison-bar/comparison-bar';
import { CookieConsentBanner } from './cookie-consent-banner/cookie-consent-banner';
import { MobileTabBar } from './mobile-tab-bar/mobile-tab-bar';
import { NewsletterSignup } from './newsletter-signup/newsletter-signup';
import { EditionSwitcher } from './edition-switcher/edition-switcher';
import { UpdateBanner } from './update-banner/update-banner';

// Finds the component at the deepest point of the route tree, with the same
// logic as DealsRouteReuseStrategy's "same component" check.
// IMPORTANT: current.component, NOT current.routeConfig.component. The latter
// is only set for eagerly imported routes; with lazy loadComponent routes it
// stayed undefined, "undefined !== undefined" was always false and the scroll
// position never reset between two lazy pages (a real production bug).
function leafComponent(snapshot: ActivatedRouteSnapshot): unknown {
  let current = snapshot;
  while (current.firstChild) current = current.firstChild;
  return current.component ?? null;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, NewsletterSignup, CookieConsentBanner, MobileTabBar, ComparisonBar, UpdateBanner, EditionSwitcher],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly dealsService = inject(DealsService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  // null = no navigation yet (see shouldResetScroll).
  private lastNavigation: NavigationSnapshot | null = null;

  protected readonly currentYear = new Date().getFullYear();
  // Brand names can carry spaces ("Transparent Labs"); toLowerCase() would
  // put them in the URL as-is and create a second, %20-encoded address.
  protected readonly brandSlug = brandSlug;

  /** Whether this page shows the footer brand list (see core/footer-brand-links.ts). */
  protected readonly showFooterBrands = signal(true);

  /**
   * On the admin panel the site shell (footer, cookie banner, comparison
   * bar, mobile tabs) is hidden: it is a tool screen, not a visitor page,
   * and marketing chrome only takes up space there.
   */
  protected readonly adminPage = signal(false);

  protected readonly brands = signal<string[]>([]);
  protected readonly categories = signal<{ slug: string; label: string }[]>([]);

  // Brand comparison pairs (/compare/:pair), with the same canonical rule as
  // brand-page.ts's comparisonPairSlug: alphabetical, unique pairs only
  // (hiq-vs-ssn, never ssn-vs-hiq) to avoid duplicate content.
  protected readonly comparisonPairs = computed(() => {
    const sorted = [...this.brands()].sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()));
    const pairs: { slug: string; label: string }[] = [];
    for (let i = 0; i < sorted.length; i++) {
      for (let j = i + 1; j < sorted.length; j++) {
        pairs.push({
          slug: `${sorted[i].toLowerCase()}-vs-${sorted[j].toLowerCase()}`,
          label: `${sorted[i]} - ${sorted[j]}`,
        });
      }
    }
    return pairs;
  });

  ngOnInit(): void {
    this.dealsService.getFilterOptions().subscribe((options) => {
      this.brands.set(options.brands);
      this.categories.set(options.categories.map((slug) => ({ slug, label: CATEGORY_LABELS[slug] ?? slug })));
    });

    // Site-wide Organization + Person (founder) schema.org markup, added once
    // and never removed, unlike per-page Product/FAQ JSON-LD. Supplements are
    // a YMYL topic, where an identifiable author is a trust signal.
    const origin = canonicalOrigin(this.document);
    upsertJsonLdScript(this.document, null, {
      '@context': 'https://schema.org',
      '@type': 'Organization',
      name: SITE_NAME,
      url: origin,
      logo: `${origin}/icons/icon-512x512.png`,
      founder: {
        '@type': 'Person',
        name: FOUNDER.name,
        jobTitle: FOUNDER.jobTitle,
        url: FOUNDER.blogUrl,
        sameAs: [FOUNDER.linkedInUrl],
      },
    });
    upsertJsonLdScript(this.document, null, {
      '@context': 'https://schema.org',
      '@type': 'Person',
      name: FOUNDER.name,
      jobTitle: FOUNDER.jobTitle,
      url: FOUNDER.blogUrl,
      sameAs: [FOUNDER.linkedInUrl],
      worksFor: { '@type': 'Organization', name: SITE_NAME, url: origin },
    });

    // An SPA does not inherit the browser's "scroll to top on a new page"
    // behavior. Angular's withInMemoryScrolling would scroll on EVERY
    // navigation, including opening the product modal through ?product= on
    // a category or brand page, and jump the page behind it. So the page
    // scrolls to the top only when a DIFFERENT component is shown.
    // NOTE: this subscription is OUTSIDE the isBrowser guard: the footer
    // decision must apply in the SSR output too, since that is what Google
    // reads. Scrolling only makes sense in the browser.
    this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd)).subscribe(() => {
      // The decision lives in core/scroll-reset.ts, pure and testable; this
      // only reads the state and applies it.
      const next: NavigationSnapshot = {
        component: leafComponent(this.router.routerState.snapshot.root),
        path: routePath(this.router.url),
      };

      this.showFooterBrands.set(showFooterBrandLinks(next.path));
      this.adminPage.set(next.path === '/admin' || next.path.startsWith('/admin/'));

      if (this.isBrowser && shouldResetScroll(this.lastNavigation, next)) {
        window.scrollTo(0, 0);
      }

      this.lastNavigation = next;
    });
  }
}
