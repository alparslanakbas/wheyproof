import { Deal } from './deal.model';
import { buildProductFacts } from './product-facts';

// The real subtlety in the stock row is null vs false: many sources don't
// report stock and send null. A truthy check would print "out of stock" on
// every product from those sources: invented data.
function deal(overrides: Partial<Deal> = {}): Deal {
  return {
    productId: 1,
    productName: 'Test Whey 2 lb',
    productUrl: 'https://example.com/product',
    imageUrl: null,
    category: 'protein-powder',
    size: '2 lb',
    flavor: null,
    inStock: null,
    seller: null,
    servingSizeGrams: null,
    servingsPerPackage: null,
    description: null,
    nutritionJson: null,
    proteinPerServingGrams: null,
    brandName: 'TestBrand',
    currentPrice: 100,
    referencePrice: 120,
    discountPercent: 16.7,
    storeOldPrice: null,
    storeDiscountPercent: null,
    scrapedAt: new Date().toISOString(),
    isAtThirtyDayLow: false,
    ...overrides,
  } as Deal;
}

function stockRow(d: Deal) {
  return buildProductFacts(d).find((f) => f.label === 'Stock');
}

function sellerRow(d: Deal) {
  return buildProductFacts(d).find((f) => f.label === 'Seller');
}

describe('buildProductFacts: stock', () => {
  it("REGRESSION: shows NO row for a source that doesn't report stock (null)", () => {
    expect(stockRow(deal({ inStock: null }))).toBeUndefined();
  });

  it('shows no row for an in-stock product', () => {
    expect(stockRow(deal({ inStock: true }))).toBeUndefined();
  });

  it('shows the row with the brand name for an out-of-stock product', () => {
    const row = stockRow(deal({ inStock: false, brandName: 'Nutricost' }));
    expect(row).toBeDefined();
    expect(row!.value).toContain('Nutricost');
    // It must say we keep tracking it, so nobody thinks the record is lost.
    expect(row!.value).toContain('keep tracking');
  });
});

// For retailer sources the maker and the seller differ: a product appears
// under the brand but is sold by a retailer. With no barcode, listings aren't
// matched across sellers, so showing who sells it matters even more.
describe('buildProductFacts: seller', () => {
  it("shows NO seller row for a product from the brand's own store", () => {
    expect(sellerRow(deal({ seller: null }))).toBeUndefined();
  });

  it('names both the brand and the seller for a retailer listing', () => {
    const row = sellerRow(deal({ seller: 'store.example.com', brandName: 'Optimum Nutrition' }));
    expect(row).toBeDefined();
    expect(row!.value).toContain('Optimum Nutrition');
    expect(row!.value).toContain('store.example.com');
  });

  it('REGRESSION: the out-of-stock message names the SELLER, not the brand', () => {
    // "Out of stock on Optimum Nutrition" would be wrong: the retailer may be
    // out while the brand's own store still has it.
    const row = stockRow(deal({ inStock: false, seller: 'store.example.com', brandName: 'Optimum Nutrition' }));
    expect(row!.value).toContain('store.example.com');
    expect(row!.value).not.toContain('Optimum Nutrition');
  });

  it('names the brand in the out-of-stock message when there is no seller', () => {
    const row = stockRow(deal({ inStock: false, seller: null, brandName: 'Nutricost' }));
    expect(row!.value).toContain('Nutricost');
  });
});
