import { environment } from '../../environments/environment';
import { alternateLinksFor, listedEditions } from './editions';
import { SITE_URL } from './site-identity';

// The site can be reachable on more than one host. So Google doesn't treat
// that as duplicate content, <link rel="canonical"> always points to the REAL
// domain, whichever host the request came in on. environment.canonicalOrigin
// is the fixed production domain, and null in local development (then
// document.location is used, so localhost tests don't produce wrong URLs).
export function canonicalOrigin(document: Document): string {
  return environment.canonicalOrigin ?? document.location.origin;
}

export function setCanonicalLink(document: Document, path: string): void {
  let link = document.querySelector<HTMLLinkElement>('link[rel="canonical"]');
  if (!link) {
    link = document.createElement('link');
    link.rel = 'canonical';
    document.head.appendChild(link);
  }
  link.href = `${canonicalOrigin(document)}${path}`;
}

/**
 * The page's hreflang alternates (see editions.ts). Old ones are removed first:
 * in a single-page app the previous page's tags would otherwise stay. None in
 * local development (no canonical origin), none on a noindex page.
 */
export function setAlternateLinks(document: Document, canonicalPath: string, noIndex: boolean): void {
  document.querySelectorAll('link[rel="alternate"][hreflang]').forEach((link) => link.remove());
  if (noIndex || environment.canonicalOrigin === null) return;

  for (const alternate of alternateLinksFor(canonicalPath, listedEditions(), SITE_URL)) {
    const link = document.createElement('link');
    link.rel = 'alternate';
    link.hreflang = alternate.hreflang;
    link.href = alternate.href;
    document.head.appendChild(link);
  }
}

