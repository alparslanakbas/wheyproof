/**
 * The query parameters pages read (home page list state, the brand page's
 * dealer view, the product window on lists). A request carrying any other
 * parameter never enters the SSR cache.
 */
const KNOWN_PARAMETERS = new Set([
  'page',
  'search',
  'brands',
  'categories',
  'sellers',
  'min',
  'max',
  'sort',
  'view',
  'seller',
  'product',
]);

/**
 * The SSR cache key; `null` = this request is not cached.
 *
 * WHY (security review, 26 Sept): the key was the raw URL, so `?r=<random>`
 * opened a new entry on every request and could fill the 120-entry cache with
 * junk, pushing hot pages out.
 *
 * The review suggested "drop unknown parameters from the key"; that is NOT
 * done on purpose. Pagination links use `queryParamsHandling="merge"` and carry
 * the whole current query: the HTML for `/?r=bad` contains `?r=bad&page=2`
 * links, and written under the clean `/` key it would be served to every
 * later visitor (and Googlebot). Instead a request with an unknown parameter
 * is never cached and the key stays the raw URL. If this list misses a
 * parameter, the only result is "not cached" — never "wrong page served".
 */
export function ssrCacheKey(method: string, originalUrl: string, basePath: string): string | null {
  if (method !== 'GET') return null;
  const url = new URL(originalUrl, 'http://local');
  // The watchlist depends on the key in the browser; a recovery link
  // (`recover`, not in the list) carries a token that belongs to one person.
  if (url.pathname.startsWith(`${basePath}/watchlist`)) return null;
  for (const name of url.searchParams.keys()) {
    if (!KNOWN_PARAMETERS.has(name)) return null;
  }
  return originalUrl;
}
