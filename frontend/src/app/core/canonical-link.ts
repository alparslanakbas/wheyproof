import { environment } from '../../environments/environment';

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
