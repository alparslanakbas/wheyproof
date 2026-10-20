import { environment } from '../../environments/environment';

/** Path the edition is served under: '' for the US site, '/uk' for the UK section. */
export const BASE_PATH = environment.basePath;

// An address written by hand into an href.
//
// RouterLink adds the base href itself, but a plain "/product/1" in an href is
// root-relative and ignores <base href="/uk/">: on the UK section it would
// send the visitor, and every crawler, to the US page. Every hand-built
// in-site href goes through this.
export function sitePath(path: string): string {
  return `${BASE_PATH}${path}`;
}

// The same fix for root-relative links inside stored HTML (article bodies).
// Every edition seeds the same articles (backend ArticleSeeder), written with
// US addresses ("/calculators/protein"); until 2026-10-20 all of them sent UK
// readers to the US pages. Protocol-relative ("//") and already-prefixed
// links are left alone.
export function withSitePaths(html: string, basePath: string = BASE_PATH): string {
  if (!basePath) return html;
  const own = basePath.slice(1);
  return html.replace(new RegExp(`href="/(?!/|${own}/|${own}")`, 'g'), `href="${basePath}/`);
}
