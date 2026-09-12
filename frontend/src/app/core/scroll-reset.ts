// The "scroll to the top" decision for SPA navigation.
//
// This logic was buried in app.ts and untestable; a bug (including the
// fragment in the path comparison) reached production and was found by a
// user. It was split out as a pure function.

/**
 * Normalizes a path for comparison: query and fragment are dropped.
 *
 * '#' MUST be cut. `Router.url` includes the fragment; otherwise clicking an
 * in-page section link (e.g. /privacy#your-rights) looks like "another page"
 * and resetting the scroll undoes the jump the browser just made.
 */
export function routePath(url: string): string {
  return url.split(/[?#]/)[0];
}

export interface NavigationSnapshot {
  /** The route's leaf component; tells navigations within one component apart. */
  component: unknown;
  /** The path, normalized by routePath(). */
  path: string;
}

/**
 * Should the page scroll to the top after this navigation?
 *
 * With `previous` null this is the first navigation and there is NO reset:
 * the document already opens at the top, and a fragment in the address (a
 * shared section link) or a scroll position restored by back/forward must not
 * be undone.
 */
export function shouldResetScroll(previous: NavigationSnapshot | null, next: NavigationSnapshot): boolean {
  if (previous === null) return false;

  const changed = previous.component !== next.component || previous.path !== next.path;
  if (!changed) return false;

  // The one exception is the product modal: opening and closing it on the
  // home page changes the path between '/' and '/product/...', but it is a
  // layer over the same page (see DealsRouteReuseStrategy). Resetting here
  // would send someone who clicked a product halfway down back to the top.
  //
  // It only applies within the same component: going from the product modal
  // to a brand page is a real page change.
  const sameComponent = previous.component === next.component;
  const productModalNav = sameComponent && (next.path.startsWith('/product/') || previous.path.startsWith('/product/'));

  return !productModalNav;
}
