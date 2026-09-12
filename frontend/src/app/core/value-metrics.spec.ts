import { describe, expect, it } from 'vitest';
import { Deal } from './deal.model';
import {
  PROTEIN_REFERENCE_GRAMS,
  pricePerServing,
  proteinRatioPercent,
  proteinReferenceCost,
  servingsInPackage,
} from './value-metrics';

// Ratios mirror real labels: 20 g protein in a 25 g scoop (80%), 22.5 g in
// 30 g (75%), 16 g in a 55 g bar (29%).
function deal(overrides: Partial<Deal> = {}): Deal {
  return {
    productId: 1,
    productName: 'Test',
    productUrl: 'https://x',
    imageUrl: null,
    category: null,
    size: '900 g',
    flavor: null,
    servingSizeGrams: 30,
    servingsPerPackage: null,
    description: null,
    nutritionJson: null,
    proteinPerServingGrams: 24,
    brandName: 'Test',
    currentPrice: 1000,
    referencePrice: 1000,
    discountPercent: 0,
    storeOldPrice: null,
    storeDiscountPercent: null,
    scrapedAt: new Date().toISOString(),
    isAtThirtyDayLow: false,
    ...overrides,
  } as Deal;
}

describe('servingsInPackage', () => {
  it("prefers the brand's stated servings per package", () => {
    // Even when it could be computed from the package, the brand's statement wins.
    expect(servingsInPackage(deal({ servingsPerPackage: 64, size: '900 g' }))).toBe(64);
  });

  it('otherwise divides the package weight by the serving size', () => {
    expect(servingsInPackage(deal({ size: '900 g', servingSizeGrams: 30 }))).toBe(30);
  });

  it('converts kilograms to grams', () => {
    expect(servingsInPackage(deal({ size: '2 kg', servingSizeGrams: 30 }))).toBeCloseTo(66.67, 1);
    expect(servingsInPackage(deal({ size: '1.5 kg', servingSizeGrams: 30 }))).toBe(50);
  });

  // US packages are mostly sold by the pound; missing this would silently
  // drop nearly every tub from the per-serving math.
  it('converts pounds to grams (avoirdupois)', () => {
    // 5 lb = 2267.96 g; 2267.96 / 30 = 75.6
    expect(servingsInPackage(deal({ size: '5 lb', servingSizeGrams: 30 }))).toBeCloseTo(75.6, 1);
  });

  it("returns null when the package weight can't be read", () => {
    expect(servingsInPackage(deal({ size: null }))).toBeNull();
    // A count isn't a weight.
    expect(servingsInPackage(deal({ size: '120 Capsules' }))).toBeNull();
  });
});

describe('proteinRatioPercent', () => {
  it('computes real label values correctly', () => {
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: 20, servingSizeGrams: 25 }))).toBe(80);
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: 22.5, servingSizeGrams: 30 }))).toBe(75);
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: 16, servingSizeGrams: 55 }))).toBe(29);
  });

  // No data, NO ESTIMATE: some comparison sites assume a standard 30 g
  // serving when they don't know it; we show nothing.
  it('returns null when protein or serving size is missing', () => {
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: null }))).toBeNull();
    expect(proteinRatioPercent(deal({ servingSizeGrams: null }))).toBeNull();
  });

  it('returns null for inconsistent label data', () => {
    // A serving can't hold more protein than its own weight; that's a parse error.
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: 40, servingSizeGrams: 30 }))).toBeNull();
    expect(proteinRatioPercent(deal({ proteinPerServingGrams: 0, servingSizeGrams: 30 }))).toBeNull();
  });
});

describe('proteinReferenceCost', () => {
  it('computes the cost of a fixed amount of protein', () => {
    // 900 g / 30 g = 30 servings at 24 g each → 720 g of protein in total.
    // 1000 / 720 g × 30 g = 41.67
    const cost = proteinReferenceCost(deal({ currentPrice: 1000, size: '900 g', servingSizeGrams: 30, proteinPerServingGrams: 24 }));
    expect(cost).toBeCloseTo(41.67, 1);
  });

  it('a higher protein density gives a cheaper reference cost', () => {
    // Same price and package, different protein density.
    const dense = proteinReferenceCost(deal({ proteinPerServingGrams: 24 }))!;
    const sparse = proteinReferenceCost(deal({ proteinPerServingGrams: 16 }))!;
    expect(dense).toBeLessThan(sparse);
  });

  it('returns null without protein data', () => {
    expect(proteinReferenceCost(deal({ proteinPerServingGrams: null }))).toBeNull();
    expect(proteinReferenceCost(deal({ size: null, servingsPerPackage: null }))).toBeNull();
  });

  // If the reference changed, two pages would drift apart; this locks it.
  it('uses 30 grams as the reference amount', () => {
    expect(PROTEIN_REFERENCE_GRAMS).toBe(30);
  });
});

describe('pricePerServing', () => {
  it('computes the cost of one serving', () => {
    expect(pricePerServing(deal({ currentPrice: 900, size: '900 g', servingSizeGrams: 30 }))).toBe(30);
  });

  it("returns null when the package doesn't hold a single serving", () => {
    // Inconsistent data: the serving is bigger than the package.
    expect(pricePerServing(deal({ size: '20 g', servingSizeGrams: 30 }))).toBeNull();
  });
});
