import { BASE_PATH } from './site-path';

/** One country edition of the site: a separate instance under its own path. */
export interface Edition {
  code: string;
  label: string;
  /** '' for the US site, '/uk' for the UK section (see site-path.ts). */
  basePath: string;
  /** Language-region tag for hreflang ("en-US", "en-GB"). */
  hreflang: string;
  /**
   * Shown in the country switcher. An edition that isn't open yet (noindex,
   * catalog still filling) stays unlisted, so no page links visitors into it.
   */
  listed: boolean;
}

// EVERY edition, in ONE list shared by all builds. Unlike market-config.ts this
// file is not swapped per build: each edition has to know where the others
// live. Adding a country is a row here plus its build configuration.
export const EDITIONS: readonly Edition[] = [
  { code: 'US', label: 'United States', basePath: '', hreflang: 'en-US', listed: true },
  // Opened 2026-09-19. This flag is the whole opening: it shows the edition in
  // the switcher, adds hreflang both ways, lists its sitemap in robots.txt and
  // lifts its noindex (see server.ts). Setting it back to false closes all four.
  { code: 'UK', label: 'United Kingdom', basePath: '/uk', hreflang: 'en-GB', listed: true },
];

/** The edition this build serves. */
export const CURRENT_EDITION: Edition =
  EDITIONS.find((edition) => edition.basePath === BASE_PATH) ?? EDITIONS[0];

// Pages that exist at the same address in every edition. Everything else
// (products, reviews, brands, comparisons) is edition-specific: the catalogs
// differ per country, and a brand in one may not exist in the other.
const SHARED_ROUTES: readonly RegExp[] = [
  /^\/$/,
  /^\/categories$/,
  /^\/category\/[a-z0-9-]+$/,
  /^\/brands$/,
  /^\/calculators(\/[a-z0-9-]+)?$/,
  /^\/glossary$/,
  /^\/how-it-works$/,
  /^\/about$/,
  /^\/contact$/,
  /^\/privacy$/,
  /^\/cookies$/,
  /^\/terms$/,
  /^\/watchlist$/,
  // Every edition seeds the same embedded articles at startup (backend
  // ArticleSeeder), so each guide exists at the same slug in all of them.
  // Guides were left out on the belief that the UK section had no articles;
  // without hreflang, Search Console reported the UK copies as duplicates of
  // the US ones (2026-09-23). An article added through the admin API to one
  // database only would break this pairing.
  /^\/guides(\/[a-z0-9-]+)?$/,
];

/**
 * The address of the current page in another edition: the same page where it
 * exists there, that edition's home page otherwise. Query and fragment are
 * dropped (filters and scroll positions belong to this edition's catalog).
 *
 * @param routerUrl the Router's URL, WITHOUT the base href ("/category/creatine").
 */
export function editionHref(target: Edition, routerUrl: string): string {
  const path = pathOf(routerUrl);
  const shared = isSharedPath(path);
  // A full address with the target's path, never a RouterLink: the other
  // edition is a different app, so the browser must load it, not this router.
  return `${target.basePath}${shared ? path : '/'}`;
}

/** Editions to show in the switcher; empty while fewer than two are open. */
export function listedEditions(): readonly Edition[] {
  const listed = EDITIONS.filter((edition) => edition.listed);
  return listed.length > 1 ? listed : [];
}

function pathOf(routerUrl: string): string {
  return routerUrl.split(/[?#]/)[0] || '/';
}

function isSharedPath(path: string): boolean {
  return SHARED_ROUTES.some((route) => route.test(path));
}

export interface AlternateLink {
  hreflang: string;
  href: string;
}

/**
 * hreflang alternates for a page: one per listed edition, plus x-default
 * pointing at the first (US) edition. Empty for a page that exists in only one
 * edition (a product in the other catalog isn't the "same page" in another
 * language, and pointing hreflang at a home page is an error Google reports),
 * for a page with a query (paginated or filtered views), and while fewer than
 * two editions are listed.
 *
 * Built from the same SHARED_ROUTES as the switcher, so the two can't
 * disagree about which page matches which.
 *
 * @param siteOrigin the host origin without an edition path ("https://www.wheyproof.com").
 */
export function alternateLinksFor(
  routerUrl: string,
  editions: readonly Edition[],
  siteOrigin: string,
): AlternateLink[] {
  if (editions.length < 2 || /[?#]/.test(routerUrl)) return [];
  const path = pathOf(routerUrl);
  if (!isSharedPath(path)) return [];

  const links = editions.map((edition) => ({
    hreflang: edition.hreflang,
    href: `${siteOrigin}${edition.basePath}${path}`,
  }));
  return [...links, { hreflang: 'x-default', href: links[0].href }];
}

