import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

import { canonicalOrigin, setCanonicalLink } from './canonical-link';
import { MARKET } from './market';
import { clampDescription, clampTitle } from './meta-description';
import { SITE_NAME } from './site-identity';

// Open Graph wants the locale with an underscore ("en_US").
const OG_LOCALE = MARKET.locale.replace('-', '_');

export interface PageMetaOptions {
  title: string;
  description: string;
  canonicalPath: string;
  ogType?: string;
  ogImage?: string;
  // A share card (WhatsApp/X/Facebook) text DIFFERENT from the <title> Google
  // sees, e.g. a product page shared with its price while the <title> stays
  // price-free. Falls back to options.title.
  ogTitle?: string;
  // The page must stay out of search indexes (personal content, or a product
  // the store no longer returns).
  //
  // Without a value the service REMOVES the tag. That is required: in a
  // single-page app a robots tag added on one page would otherwise linger on
  // the next and drop normal pages from the index.
  noIndex?: boolean;
}

// Title/description/OG/canonical logic used to be copied by hand into ten
// pages, and five of them lost og:title/og:description on the way: shared
// links showed the home page's title. The OG fields are part of the options
// object here, so that class of bug can't come back. og:url, og:locale and the
// twitter:* tags are set on every page too; og:site_name is also static in
// index.html, but confirming it here guards against that tag being removed.
@Injectable({ providedIn: 'root' })
export class PageMetaService {
  private readonly titleService = inject(Title);
  private readonly metaService = inject(Meta);
  private readonly document = inject(DOCUMENT);

  set(options: PageMetaOptions): void {
    const origin = canonicalOrigin(this.document);
    const ogImage = options.ogImage ?? `${origin}/og-image.png`;
    // ogTitle is NOT clamped on purpose: share cards can show longer titles
    // than search results, and the price should be visible there.
    const ogTitle = options.ogTitle ?? options.title;

    // One safety net instead of fixing every page's template: pages added
    // later are covered too. Product and review pages also use
    // buildPageTitle, which keeps the brand tail more carefully. Titles up to
    // 118 and descriptions up to 238 characters were found; Google cuts both,
    // and rewrites overly long titles entirely.
    const title = clampTitle(options.title);
    const description = clampDescription(options.description);

    this.titleService.setTitle(title);
    this.metaService.updateTag({ name: 'description', content: description });
    this.metaService.updateTag({ property: 'og:title', content: ogTitle });
    this.metaService.updateTag({ property: 'og:description', content: description });
    this.metaService.updateTag({ property: 'og:type', content: options.ogType ?? 'website' });
    this.metaService.updateTag({ property: 'og:image', content: ogImage });
    this.metaService.updateTag({ property: 'og:url', content: `${origin}${options.canonicalPath}` });
    this.metaService.updateTag({ property: 'og:locale', content: OG_LOCALE });
    this.metaService.updateTag({ property: 'og:site_name', content: SITE_NAME });
    this.metaService.updateTag({ name: 'twitter:title', content: ogTitle });
    this.metaService.updateTag({ name: 'twitter:description', content: description });
    this.metaService.updateTag({ name: 'twitter:image', content: ogImage });

    if (options.noIndex) {
      this.metaService.updateTag({ name: 'robots', content: 'noindex, follow' });
    } else {
      // Removing matters as much as adding; see the noIndex field.
      this.metaService.removeTag("name='robots'");
    }

    setCanonicalLink(this.document, options.canonicalPath);
  }
}

// "Update if present, otherwise create" for a JSON-LD script tag. The element
// reference isn't kept here (some callers add and remove it as the selection
// changes); the caller keeps it and passes it back on the next call.
export function upsertJsonLdScript(document: Document, existingEl: HTMLScriptElement | null, data: unknown): HTMLScriptElement {
  let el = existingEl;
  if (!el) {
    el = document.createElement('script');
    el.type = 'application/ld+json';
    document.head.appendChild(el);
  }
  el.textContent = JSON.stringify(data);
  return el;
}
