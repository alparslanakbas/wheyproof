import { describe, expect, it } from 'vitest';
import { buildPageTitle, buildProductDescription, buildReviewDescription, clampTitle } from './meta-description';

function build(description: string | null, overrides: Partial<Parameters<typeof buildProductDescription>[0]> = {}) {
  return buildProductDescription({
    displayName: 'Test Product',
    brandName: 'TestBrand',
    priceText: '$29.99',
    discountPercent: 0,
    description,
    ...overrides,
  });
}

describe('buildProductDescription', () => {
  it('falls back to the price template without a description', () => {
    const result = build(null);
    expect(result).toContain('Test Product costs $29.99 today');
    expect(result).toContain('TestBrand');
  });

  it('states the discount on a discounted product', () => {
    const result = build(null, { discountPercent: 25 });
    expect(result).toContain('verified 25% drop');
  });

  it('drops a "Description:" prefix', () => {
    const result = build('Description: HIQ Dualforce is a powdered pre-workout formula made for hard training days.');
    expect(result.startsWith('HIQ Dualforce')).toBe(true);
    expect(result).not.toContain('Description:');
  });

  it('drops a "Product Overview:" prefix', () => {
    const result = build('Product Overview: Micronized creatine monohydrate that mixes easily into any drink.');
    expect(result.startsWith('Micronized creatine')).toBe(true);
    expect(result).not.toContain('Overview');
  });

  it('drops a "... WHAT IS IT?:" heading', () => {
    const result = build('CREAPURE CREATINE WHAT IS IT?: A pure creatine monohydrate made under a German patent by AlzChem.');
    expect(result.startsWith('A pure creatine')).toBe(true);
    expect(result).not.toContain('WHAT IS IT');
  });

  it('drops a repeated product name at the start', () => {
    const result = build('Test Product GMP and HACCP certified facilities produce this pure whey protein.');
    expect(result.startsWith('GMP and HACCP')).toBe(true);
  });

  it('gives up when dropping the name would cut the sentence mid-way', () => {
    const result = build('Test Product; fiber and prebiotics combined in one convenient drink powder.');
    expect(result.startsWith('Test Product;')).toBe(true);
  });

  it('turns non-breaking spaces into normal spaces', () => {
    const result = build('This product was developed for athletes who train hard.');
    expect(result).not.toContain(' ');
    expect(result).toContain('This product was developed');
  });

  it('takes only the first sentence', () => {
    const result = build('Developed for athletes who train hard and want more protein. The second sentence must not appear.');
    expect(result).not.toContain('second sentence');
  });

  it("doesn't use a very short description; falls back to the template", () => {
    const result = build('Protein powder.');
    expect(result).toContain('costs $29.99 today');
  });

  it('trims a long single sentence and stays within the snippet limit', () => {
    const long = 'This product ' + 'contains a very long description text '.repeat(10) + 'and ends here.';
    const result = build(long);
    expect(result).toContain('…');
    expect(result.length).toBeLessThanOrEqual(165);
  });

  it('always keeps the price', () => {
    const result = build('Developed for athletes who train hard and want a clean protein source.');
    expect(result).toContain('$29.99');
  });

  // Some stores write the product name twice in a row; the name check misses
  // it because the spellings differ slightly ("5 lb (…)" vs "5lb …").
  it('drops the first copy when the copy writes the product name twice', () => {
    const result = build(
      'Optimum Nutrition Gold Standard 100% Whey 5 lb (Double Rich Chocolate) ' +
        'Optimum Nutrition Gold Standard 100% Whey delivers 24 grams of protein per serving.',
      { displayName: 'Optimum Nutrition Gold Standard 100% Whey 5lb Double Rich Chocolate' },
    );
    expect(result).not.toContain('(Double Rich Chocolate) Optimum');
    expect(result.indexOf('Gold Standard')).toBe(result.lastIndexOf('Gold Standard'));
  });
});

describe('clampTitle', () => {
  it('leaves a title under the limit alone', () => {
    const t = 'Optimum Nutrition Creatine Prices 2026 | WheyProof';
    expect(clampTitle(t)).toBe(t);
  });

  // Brand x category titles are the ones most likely to run long, and a
  // half-cut tail ("…2026 |…") looks broken in search results.
  it('drops the whole tail instead of leaving half of it', () => {
    const t = 'Optimum Nutrition Protein Snacks Prices and Deals 2026 | WheyProof';
    const result = clampTitle(t);
    expect(result).toBe('Optimum Nutrition Protein Snacks Prices and Deals 2026');
    expect(result).not.toContain('|');
    expect(result).not.toContain('…');
  });

  it('never ends a title with a dangling separator or ellipsis', () => {
    const samples = [
      'Transparent Labs Amino Acids Prices and Deals in the US 2026 | WheyProof',
      'Nutricost Fat Burners & Hydration Prices and Deals 2026 | WheyProof',
      'MuscleTech Protein Powder Prices and Deals for Every Budget 2026 | WheyProof',
    ];
    for (const t of samples) {
      expect(clampTitle(t)).not.toMatch(/[|·,:;&/–-]\s*…?\s*$/);
    }
  });

  it('trims at a word boundary when a title without a tail is too long', () => {
    const t = 'A'.repeat(30) + ' ' + 'B'.repeat(30) + ' ' + 'C'.repeat(30);
    const result = clampTitle(t);
    expect(result.endsWith('…')).toBe(true);
    expect(result.length).toBeLessThanOrEqual(66);
  });
});

describe('buildPageTitle', () => {
  it('keeps the brand tail when everything fits', () => {
    expect(buildPageTitle('Nutricost Creatine', 'Price History', 'Nutricost')).toBe(
      'Nutricost Creatine Price History | Nutricost',
    );
  });

  // A number torn from its unit at the cut ("…366…") looks broken.
  it("doesn't leave an orphan number at the end", () => {
    const result = buildPageTitle(
      'Optimum Nutrition Gold Standard 100% Whey Protein 366 g Chocolate',
      'Review',
      'Optimum Nutrition',
    );
    expect(result).not.toContain('366…');
    expect(result).toContain('Review');
  });

  it("doesn't leave an orphan single letter at the end", () => {
    const result = buildPageTitle(
      'Quest Protein Bar Chocolate Chip Cookie Dough 60g x 12 Bars',
      'Price History',
      'Quest',
    );
    expect(result).not.toMatch(/\sx…/);
    expect(result).toContain('60g');
  });

  it('keeps a fragment that carries its unit', () => {
    const result = buildPageTitle(
      'Nutricost L-Arginine Capsules 1250 mg 120 Capsules 60 Servings',
      'Price History',
      'Nutricost',
    );
    expect(result).not.toMatch(/\s\d+…/);
  });
});

describe('buildReviewDescription', () => {
  const base = {
    displayName: 'Optimum Nutrition Gold Standard 100% Whey 5 lb',
    priceText: '$84.99',
    discountPercent: 0,
    historyDays: 30,
  };

  it('leads with the price; a searcher typing the product name wants it', () => {
    const d = buildReviewDescription(base);
    expect(d).toContain('$84.99');
    expect(d.indexOf('$84.99')).toBeLessThan(d.indexOf('Independent review'));
  });

  it('mentions the number of days when there is enough history', () => {
    expect(buildReviewDescription({ ...base, historyDays: 30 })).toContain('30 days of price history');
  });

  // The real protection: with thin data the claim is NOT inflated.
  it("doesn't mention the number of days when history is thin", () => {
    const d = buildReviewDescription({ ...base, historyDays: 2 });
    expect(d).not.toContain('days of price history');
    expect(d).toContain('$84.99');
  });

  it('leads with the discount when there is a discount and enough history', () => {
    const d = buildReviewDescription({ ...base, discountPercent: 18, historyDays: 20 });
    expect(d).toContain('verified 18% drop');
    expect(d).toContain("not the store's label");
  });

  // The most important test: two days of data don't make a "verified"
  // discount. This site exists to expose baseless discount claims; making one
  // would rot the brand from the inside.
  it('does NOT CLAIM a discount while history is thin', () => {
    const d = buildReviewDescription({ ...base, discountPercent: 40, historyDays: 2 });
    expect(d).not.toContain('verified');
    expect(d).not.toContain('drop');
    expect(d).toContain('costs $84.99 today');
  });

  it("stays under Google's limit", () => {
    const d = buildReviewDescription({
      ...base,
      displayName: 'A Very Long Product Name '.repeat(8),
      discountPercent: 25,
      historyDays: 30,
    });
    expect(d.length).toBeLessThanOrEqual(155);
  });
});

describe('discount percent format', () => {
  // "30.8%" in a search snippet is needless precision.
  it('uses a whole number in the review description', () => {
    const d = buildReviewDescription({
      displayName: 'X', priceText: '$10.00', discountPercent: 30.8, historyDays: 28,
    });
    expect(d).toContain('verified 31% drop');
    expect(d).not.toContain('30.8');
  });

  it('uses a whole number in the product description too', () => {
    const d = buildProductDescription({
      displayName: 'X', brandName: 'Y', priceText: '$10.00', discountPercent: 7.2, description: null,
    });
    expect(d).toContain('verified 7% drop');
    expect(d).not.toContain('7.2');
  });
});

describe('clampTitle: the site name belongs in the tail', () => {
  // A title with the site name FIRST loses all of its content: clampTitle
  // drops everything after the last " | ", leaving only the name, and nothing
  // anywhere reports an error.
  it('loses the content entirely when the site name comes first, so it must go last', () => {
    const nameFirst = 'WheyProof | Real Protein Deals and Supplement Price Tracking in the US';
    expect(nameFirst.length).toBeGreaterThan(65);
    expect(clampTitle(nameFirst)).toBe('WheyProof');
  });

  it("doesn't touch a title with the name in the tail when it fits", () => {
    const right = 'Real Protein and Supplement Deals | WheyProof';
    expect(right.length).toBeLessThanOrEqual(60);
    expect(clampTitle(right)).toBe(right);
  });
});
