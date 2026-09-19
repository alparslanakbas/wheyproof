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
