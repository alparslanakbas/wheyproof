import { brandSlug } from './brand-slug';

/**
 * Which brand comparisons (/compare/a-vs-b) are offered to search engines.
 *
 * A comparison averages each brand's prices per category, so it needs enough
 * products behind both brands: a small brand's "average" often rests on a
 * single product, a number that looks like a statistic but isn't. A pair of
 * brands that both reach the threshold is in the sitemap and indexable.
 *
 * Every other pair still works for a visitor but says noindex (19 Nov 2026).
 * Leaving them out of the sitemap wasn't enough: Googlebot had found ~3,400
 * different brand pairs through links, and a page for every combination is
 * the pattern of Google's scaled content policy (see the product pairs,
 * product-comparison-page.ts, 8 Nov 2026).
 *
 * The sitemap (server.ts) and the page both use this, so the two can't drift
 * apart.
 */
export const MIN_PRODUCTS_FOR_COMPARISON = 40;

/** Slugs of the brands whose comparisons are indexable. Counts come from the
 *  brand x category pairs (/api/brand-category-pairs), summed per brand. */
export function comparableBrandSlugs(pairs: readonly { brandName: string; productCount: number }[]): Set<string> {
  const productCount = new Map<string, number>();
  for (const pair of pairs) {
    const slug = brandSlug(pair.brandName);
    productCount.set(slug, (productCount.get(slug) ?? 0) + pair.productCount);
  }
  return new Set(
    [...productCount].filter(([, count]) => count >= MIN_PRODUCTS_FOR_COMPARISON).map(([slug]) => slug),
  );
}
