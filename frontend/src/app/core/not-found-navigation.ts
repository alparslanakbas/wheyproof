import { Router } from '@angular/router';

/**
 * Shows the 404 page when the requested content doesn't exist.
 *
 * <b>WHY NOT A REDIRECT.</b> Redirecting a missing address to the home page
 * is, by Google's definition, the soft 404 itself: it signals a valid page.
 *
 * <b>skipLocationChange IS REQUIRED.</b> The address bar keeps the REQUESTED
 * address; `/not-found` never shows. Not just cosmetics: we want to tell the
 * search engine "this address doesn't exist". If the address changed, the
 * response for the requested one would be a redirect again.
 *
 * The page itself sets the status code (404) through `RESPONSE_INIT`; see
 * `NotFoundPage`.
 */
export function showNotFound(router: Router): void {
  void router.navigate(['/not-found'], { skipLocationChange: true });
}
