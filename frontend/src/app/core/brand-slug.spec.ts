import { describe, expect, it } from 'vitest';
import { brandSlug, resolveBrandFromSlug } from './brand-slug';

const BRANDS = ['Ghost', 'Kaged', 'MuscleTech', 'Optimum Nutrition', 'Transparent Labs', 'Açaí Co'];

describe('brandSlug', () => {
  it('turns spaces into hyphens', () => {
    expect(brandSlug('Optimum Nutrition')).toBe('optimum-nutrition');
    expect(brandSlug('Transparent Labs')).toBe('transparent-labs');
  });

  it('strips accents', () => {
    expect(brandSlug('Açaí Co')).toBe('acai-co');
  });

  it('lower-cases one-word brands', () => {
    expect(brandSlug('Kaged')).toBe('kaged');
    expect(brandSlug('MuscleTech')).toBe('muscletech');
  });
});

describe('resolveBrandFromSlug', () => {
  it('finds the real brand name from a slug', () => {
    expect(resolveBrandFromSlug('optimum-nutrition', BRANDS)).toBe('Optimum Nutrition');
    expect(resolveBrandFromSlug('kaged', BRANDS)).toBe('Kaged');
  });

  // Addresses with spaces or accents (typed or shared by hand) must still work.
  it('also resolves addresses with spaces or accents', () => {
    expect(resolveBrandFromSlug('optimum nutrition', BRANDS)).toBe('Optimum Nutrition');
    expect(resolveBrandFromSlug('açaí-co', BRANDS)).toBe('Açaí Co');
  });

  it('returns null for an unknown brand', () => {
    expect(resolveBrandFromSlug('unknown-brand', BRANDS)).toBeNull();
  });
});
