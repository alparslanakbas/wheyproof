import { describe, expect, it } from 'vitest';

import { withSitePaths } from './site-path';

describe('withSitePaths', () => {
  it('prefixes root-relative links with the edition path', () => {
    const html = '<p><a href="/calculators/protein">calculator</a> and <a href="/category/creatine">creatine</a></p>';

    expect(withSitePaths(html, '/uk')).toBe(
      '<p><a href="/uk/calculators/protein">calculator</a> and <a href="/uk/category/creatine">creatine</a></p>',
    );
  });

  it('leaves the US site, external, protocol-relative and already-prefixed links alone', () => {
    const html = '<a href="https://example.com/x">a</a><a href="//cdn.example.com/y">b</a><a href="/uk/guides">c</a><a href="/uk">d</a>';

    expect(withSitePaths(html, '/uk')).toBe(html);
    expect(withSitePaths('<a href="/guides">e</a>', '')).toBe('<a href="/guides">e</a>');
  });

  it('does not mistake a path that only starts with the same letters for a prefixed one', () => {
    expect(withSitePaths('<a href="/ukraine">f</a>', '/uk')).toBe('<a href="/uk/ukraine">f</a>');
  });
});
