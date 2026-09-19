// The UK section: the same app built with baseHref /uk/ and run as its own
// instance, with its own backend and database (angular.json "uk").
export const environment = {
  // Its own host, not api.wheyproof.com/uk: HTTP_TRANSFER_CACHE_ORIGIN_MAP
  // accepts bare origins only, so an API under a path would break the hand-off
  // of server-fetched data to the browser (see app.config.server.ts).
  apiBaseUrl: 'https://uk-api.wheyproof.com',
  // Includes the path: every canonical, breadcrumb and schema.org URL is this
  // plus the route, so they all land under /uk.
  canonicalOrigin: 'https://www.wheyproof.com/uk' as string | null,
  basePath: '/uk',
};
