import { showFooterBrandLinks } from './footer-brand-links';

describe('showFooterBrandLinks', () => {
  it("isn't shown on PRODUCT pages, where the list was 39% of the page", () => {
    expect(showFooterBrandLinks('/product/1126/kaged-creatine-hcl')).toBe(false);
  });

  it("isn't shown on REVIEW pages either", () => {
    expect(showFooterBrandLinks('/review/1126/kaged-creatine-hcl')).toBe(false);
  });

  it('IS shown on the home page, its main place', () => {
    expect(showFooterBrandLinks('/')).toBe(true);
  });

  it('IS shown on brand and category pages', () => {
    // Google should keep finding brand pages from here; removing the list
    // from product pages mustn't cost an entry point.
    expect(showFooterBrandLinks('/brand/kaged/protein-powder')).toBe(true);
    expect(showFooterBrandLinks('/category/creatine')).toBe(true);
  });

  it("doesn't repeat the same list in the footer of the brand directory", () => {
    expect(showFooterBrandLinks('/brands')).toBe(false);
  });

  it('leaves similar-looking but different paths alone', () => {
    expect(showFooterBrandLinks('/products')).toBe(true);
    expect(showFooterBrandLinks('/compare-products/263-vs-1126')).toBe(true);
  });
});
