import { sitePath } from './site-path';
import { slugify } from './slugify';

export interface ProductLinkSource {
  productId: number;
  productName: string;
}

// Canonical path of a product page, in the same form as the sitemap and the
// canonical tag (see deals-list.ts, server.ts).
export function productPath(deal: ProductLinkSource): string {
  return `/product/${deal.productId}/${slugify(deal.productName)}`;
}

// The same page as an href. productPath is a ROUTER path (RouterLink and
// router.navigate add the base href themselves); a hand-built href must carry
// it, or on the UK section the link would open the US page (see site-path.ts).
export function productHref(deal: ProductLinkSource): string {
  return sitePath(productPath(deal));
}

// Product cards MUST be real <a href> links. Search engines follow only real
// links and internal authority flows only through them; as <button> + JS
// clicks, product pages sat isolated in the link graph and were discovered
// only through the sitemap.
//
// The click behavior (opening the modal) must survive, though. This helper
// says whether the app or the browser handles the event: modified and middle
// clicks go to the browser so "open in new tab" works.
export function shouldHandleInApp(event: MouseEvent): boolean {
  return event.button === 0
    && !event.metaKey
    && !event.ctrlKey
    && !event.shiftKey
    && !event.altKey;
}
