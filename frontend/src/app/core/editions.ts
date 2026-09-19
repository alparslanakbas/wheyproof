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
  // Unlisted until the UK section opens (SITE_NOINDEX removed, UK legal pages
  // in place). Flipping this to true shows the switcher on both editions.
  { code: 'UK', label: 'United Kingdom', basePath: '/uk', hreflang: 'en-GB', listed: false },
];

/** The edition this build serves. */
export const CURRENT_EDITION: Edition =
  EDITIONS.find((edition) => edition.basePath === BASE_PATH) ?? EDITIONS[0];

// Pages that exist at the same address in every edition. Everything else
// (products, reviews, brands, comparisons, guides) is edition-specific: the
// catalogs differ per country, and a brand or article in one may not exist in
// the other. Guides are here as "specific" because the UK section has no
// articles yet; move them into this list once it does.
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
];

/**
 * The address of the current page in another edition: the same page where it
 * exists there, that edition's home page otherwise. Query and fragment are
 * dropped (filters and scroll positions belong to this edition's catalog).
 *
 * @param routerUrl the Router's URL, WITHOUT the base href ("/category/creatine").
 */
export function editionHref(target: Edition, routerUrl: string): string {
  const path = routerUrl.split(/[?#]/)[0] || '/';
  const shared = SHARED_ROUTES.some((route) => route.test(path));
  // A full address with the target's path, never a RouterLink: the other
  // edition is a different app, so the browser must load it, not this router.
  return `${target.basePath}${shared ? path : '/'}`;
}

/** Editions to show in the switcher; empty while fewer than two are open. */
export function listedEditions(): readonly Edition[] {
  const listed = EDITIONS.filter((edition) => edition.listed);
  return listed.length > 1 ? listed : [];
}
