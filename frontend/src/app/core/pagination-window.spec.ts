import { describe, expect, it } from 'vitest';

import { clampTitle } from './meta-description';
import { pageFromQuery, paginatedTitle, pageWindow } from './pagination-window';

// These tests exist because of an SEO measurement: pagination that only
// worked through click handlers left no links in the server HTML, and most
// of the catalog was unreachable for Google. The rules below decide which
// pages get those links.
describe('pageWindow', () => {
  it('has just that page when there is one page', () => {
    expect(pageWindow(1, 1)).toEqual([1]);
  });

  it('returns nothing when there are no pages', () => {
    expect(pageWindow(1, 0)).toEqual([]);
  });

  it('shows no gap marker in a short list', () => {
    expect(pageWindow(1, 5)).toEqual([1, 2, 3, 4, 5]);
  });

  // The main rule: first and last page ALWAYS get a link. A crawler can't be
  // expected to walk a 205-page chain one by one; two fixed ends cut the depth.
  it('always includes the first and last page in a long list', () => {
    const window = pageWindow(100, 205);
    expect(window[0]).toBe(1);
    expect(window[window.length - 1]).toBe(205);
  });

  it('puts a gap on both sides of a middle page', () => {
    expect(pageWindow(100, 205)).toEqual([1, null, 98, 99, 100, 101, 102, null, 205]);
  });

  it('has a gap only on the right near the start', () => {
    expect(pageWindow(2, 50)).toEqual([1, 2, 3, 4, null, 50]);
  });

  it('has a gap only on the left near the end', () => {
    expect(pageWindow(49, 50)).toEqual([1, null, 47, 48, 49, 50]);
  });

  // No page should get two links: a repeated number looks wrong and means
  // two internal links to the same URL.
  it('numbers are unique and ascending', () => {
    const numbers = pageWindow(3, 40).filter((x): x is number => x !== null);
    expect(new Set(numbers).size).toBe(numbers.length);
    expect([...numbers].sort((a, b) => a - b)).toEqual(numbers);
  });

  // The page number comes from the URL, so people can type a broken value.
  // The window must still stay in range.
  it('clamps an out-of-range current page', () => {
    expect(pageWindow(999, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(pageWindow(-4, 5)).toEqual([1, 2, 3, 4, 5]);
  });
});

describe('pageFromQuery', () => {
  it.each([
    ['3', 3],
    ['1', 1],
    [null, 1],
    ['', 1],
    ['abc', 1],
    ['0', 1],
    ['-2', 1],
    ['2.7', 2],
  ])('"%s" -> %i', (raw, expected) => {
    expect(pageFromQuery(raw)).toBe(expected);
  });
});

describe('paginatedTitle', () => {
  it("leaves the first page's title unchanged", () => {
    expect(paginatedTitle('Creatine Prices | WheyProof', 1)).toBe('Creatine Prices | WheyProof');
  });

  it('puts the page number BEFORE the site tail', () => {
    expect(paginatedTitle('Creatine Prices | WheyProof', 4)).toBe('Creatine Prices – Page 4 | WheyProof');
  });

  it('appends it when there is no separator', () => {
    expect(paginatedTitle('Creatine Prices', 4)).toBe('Creatine Prices – Page 4');
  });

  // THE MAIN RULE: clampTitle drops everything after the last " | " in a
  // title that doesn't fit. A page suffix at the end would sit exactly in
  // the dropped part, and long-titled pages would all share one title again.
  it('keeps the page number when a long title gets trimmed', () => {
    const long = 'Optimum Nutrition Protein Snacks Prices and Deals in the US 2026 | WheyProof';
    const trimmed = clampTitle(paginatedTitle(long, 7));
    expect(trimmed).toContain('Page 7');
  });

  it('keeps the site tail too when the title is short', () => {
    const short = 'Creatine Prices 2026 | WheyProof';
    const trimmed = clampTitle(paginatedTitle(short, 2));
    expect(trimmed).toBe('Creatine Prices 2026 – Page 2 | WheyProof');
  });
});
