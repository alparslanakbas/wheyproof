import { describe, expect, it } from 'vitest';
import { MIN_PRODUCTS_FOR_COMPARISON, comparableBrandSlugs } from './brand-comparison-index';

describe('comparableBrandSlugs', () => {
  it('sums a brand over its categories', () => {
    const slugs = comparableBrandSlugs([
      { brandName: 'Optimum Nutrition', productCount: 25 },
      { brandName: 'Optimum Nutrition', productCount: 15 },
    ]);
    expect([...slugs]).toEqual(['optimum-nutrition']);
  });

  it('needs the threshold, not one product less', () => {
    const slugs = comparableBrandSlugs([
      { brandName: 'Ghost', productCount: MIN_PRODUCTS_FOR_COMPARISON },
      { brandName: 'Kaged', productCount: MIN_PRODUCTS_FOR_COMPARISON - 1 },
    ]);
    expect(slugs.has('ghost')).toBe(true);
    expect(slugs.has('kaged')).toBe(false);
  });

  it('answers in slugs, the form the comparison address uses', () => {
    const slugs = comparableBrandSlugs([{ brandName: 'Transparent Labs', productCount: 50 }]);
    expect(slugs.has('transparent-labs')).toBe(true);
    expect(slugs.has('Transparent Labs')).toBe(false);
  });
});
