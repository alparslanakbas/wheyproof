import { ssrCacheKey } from './ssr-cache-key';

describe('ssrCacheKey', () => {
  it('keys on the raw URL when only parameters pages read are present', () => {
    for (const url of [
      '/',
      '/?page=3',
      '/?brands=a&brands=b&search=whey&sort=price-asc',
      '/brand/ghost?seller=dealer&page=2',
      '/uk/category/protein-powder?product=4304',
    ]) {
      expect(ssrCacheKey('GET', url, '')).toBe(url);
    }
  });

  it('does not cache a request with an unknown parameter (it is not dropped from the key)', () => {
    // Dropped, the "merge" pagination links would be written under the clean key with ?r=...
    expect(ssrCacheKey('GET', '/?r=123', '')).toBeNull();
    expect(ssrCacheKey('GET', '/?page=2&utm_source=x', '')).toBeNull();
    expect(ssrCacheKey('GET', '/?PAGE=2', '')).toBeNull();
  });

  it('does not cache personal or non-GET requests', () => {
    expect(ssrCacheKey('POST', '/', '')).toBeNull();
    expect(ssrCacheKey('GET', '/watchlist', '')).toBeNull();
    expect(ssrCacheKey('GET', '/uk/watchlist', '/uk')).toBeNull();
    expect(ssrCacheKey('GET', '/?recover=abc', '')).toBeNull();
  });
});
