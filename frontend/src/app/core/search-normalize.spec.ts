import { describe, expect, it } from 'vitest';

import { matchesSearch, normalizeSearchText } from './search-normalize';

// Born from a production bug: in the brand directory "hiq" found nothing
// while "HIQ" did. The cause was a locale-aware lower-casing (tr-TR turns
// "I" into a dotless "ı").
describe('normalizeSearchText', () => {
  // A brand name and what someone types must reduce to the SAME value; the
  // rest of these tests are examples of that rule.
  it.each([
    ['HIQ', 'hiq'],
    ['HIQ', 'Hiq'],
    ['Transparent Labs', 'transparent labs'],
    ['Açaí Berry', 'acai berry'],
    ['Açaí Berry', 'açaí berry'],
    ['Crème Brûlée', 'creme brulee'],
    ['Kaged', 'KAGED'],
  ])('"%s" and "%s" reduce to the same value', (brand, typed) => {
    expect(normalizeSearchText(brand)).toBe(normalizeSearchText(typed));
  });

  // The bug itself: a tr-TR lower-casing would break this match.
  it("doesn't turn an upper-case ASCII I into a dotless ı", () => {
    expect(normalizeSearchText('HIQ')).toBe('hiq');
    expect(normalizeSearchText('Isopure')).toBe('isopure');
  });

  it('strips accents to their ASCII letter', () => {
    expect(normalizeSearchText('ÀÉÎÕÜÇ')).toBe('aeiouc');
    // A dotless ı doesn't decompose under NFD and is mapped by hand.
    expect(normalizeSearchText('ı')).toBe('i');
  });

  // The searched text is brand name plus category labels and can exceed 80
  // characters, which is why slugify couldn't be used.
  it("doesn't truncate long text", () => {
    const long = `Nutricost ${'protein powder creatine amino acids vitamins '.repeat(4)}`;

    const result = normalizeSearchText(long);

    expect(result.length).toBeGreaterThan(80);
    expect(result.endsWith('vitamins')).toBe(true);
  });

  it('collapses repeated spaces and trims the ends', () => {
    expect(normalizeSearchText('  Transparent   Labs  ')).toBe('transparent labs');
  });

  it('keeps substring matching for partial typing', () => {
    expect(normalizeSearchText('Nutricost').includes(normalizeSearchText('nutri'))).toBe(true);
    expect(normalizeSearchText('Açaí Berry').includes(normalizeSearchText('acai'))).toBe(true);
  });
});

// Reported by a visitor: typing a brand with a space found nothing because
// the brand is written as one word.
describe('matchesSearch', () => {
  it('finds a one-word brand name typed with a space', () => {
    expect(matchesSearch('MuscleTech', 'muscle tech')).toBe(true);
    expect(matchesSearch('MuscleTech', 'Muscle Tech')).toBe(true);
    expect(matchesSearch('MuscleTech', 'muscletech')).toBe(true);
  });

  it('finds a two-word brand name typed without a space', () => {
    expect(matchesSearch('Transparent Labs', 'transparentlabs')).toBe(true);
    expect(matchesSearch('Optimum Nutrition', 'optimumnutrition')).toBe(true);
  });

  it("doesn't care about word order", () => {
    expect(matchesSearch('Transparent Labs', 'labs transparent')).toBe(true);
  });

  it('finds partial typing too', () => {
    expect(matchesSearch('MuscleTech', 'tech')).toBe(true);
    expect(matchesSearch('Optimum Nutrition', 'optimum')).toBe(true);
  });

  it("isn't blocked by case or accent differences", () => {
    expect(matchesSearch('HIQ', 'hiq')).toBe(true);
    expect(matchesSearch('Açaí Berry Greens', 'acai')).toBe(true);
  });

  it('lets everything through for an empty search', () => {
    expect(matchesSearch('Kaged', '')).toBe(true);
    expect(matchesSearch('Kaged', '   ')).toBe(true);
  });

  // NO IRRELEVANT RESULTS: word-by-word matching loosens things, but EVERY
  // word must still match.
  it("doesn't match what doesn't match", () => {
    expect(matchesSearch('MuscleTech', 'muscle tech creatine')).toBe(false);
    expect(matchesSearch('Kaged', 'ghost')).toBe(false);
    expect(matchesSearch('Transparent Labs', 'transparent xyz')).toBe(false);
  });
});
