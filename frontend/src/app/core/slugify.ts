// A URL-safe slug from a product name (/product/:id/:slug, for SEO: keywords
// in the URL and a readable link). Accents are stripped rather than trusting
// a locale-aware lower-casing (NFD, then combining marks removed; dotless "ı"
// doesn't decompose and is mapped by hand), so "Açaí" becomes "acai".
// toLowerCase() then only touches plain ASCII, culture-independent.
//
// Must produce the same slug as the server wherever both build one, or the
// generated address won't match the canonical and falls into a redirect.

// Some bundle names are very long; the slug is cut at the last dash before
// the limit so no word is cut in half.
const MAX_SLUG_LENGTH = 80;

export function slugify(text: string): string {
  const normalized = text
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/ı/g, 'i')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

  if (normalized.length <= MAX_SLUG_LENGTH) return normalized;

  const truncated = normalized.slice(0, MAX_SLUG_LENGTH);
  const lastHyphen = truncated.lastIndexOf('-');
  return lastHyphen > 0 ? truncated.slice(0, lastHyphen) : truncated;
}
