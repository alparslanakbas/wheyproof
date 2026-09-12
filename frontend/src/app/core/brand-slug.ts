import { slugify } from './slugify';

/**
 * Turns a brand name into a URL segment.
 *
 * Brand names can contain spaces ("Transparent Labs"); put into the address
 * as-is, they put `%20` addresses into the sitemap. Dashes give a familiar,
 * readable address. Same slugify as product addresses.
 */
export function brandSlug(brandName: string): string {
  return slugify(brandName);
}

/**
 * Finds the real brand name from a URL segment. Old addresses with spaces
 * instead of dashes (`transparent labs-vs-...`) still resolve.
 */
export function resolveBrandFromSlug(slug: string, brands: readonly string[]): string | null {
  const normalized = brandSlug(slug);
  return brands.find((b) => brandSlug(b) === normalized) ?? null;
}
