import { EDITIONS, Edition, alternateLinksFor, editionHref } from './editions';

const us = EDITIONS.find((e) => e.code === 'US') as Edition;
const uk = EDITIONS.find((e) => e.code === 'UK') as Edition;

describe('editionHref', () => {
  it('keeps a page that exists in every edition', () => {
    expect(editionHref(uk, '/category/creatine')).toBe('/uk/category/creatine');
    expect(editionHref(uk, '/calculators/protein')).toBe('/uk/calculators/protein');
    expect(editionHref(us, '/privacy')).toBe('/privacy');
    expect(editionHref(uk, '/guides/how-to-choose-whey-protein')).toBe('/uk/guides/how-to-choose-whey-protein');
    expect(editionHref(us, '/guides')).toBe('/guides');
  });

  it('maps home to home', () => {
    expect(editionHref(uk, '/')).toBe('/uk/');
    expect(editionHref(us, '/')).toBe('/');
  });

  // Catalogs differ per country: a product or brand page may not exist there.
  it('sends edition-specific pages to the target home page', () => {
    expect(editionHref(uk, '/product/12/naked-whey')).toBe('/uk/');
    expect(editionHref(uk, '/brand/transparent-labs')).toBe('/uk/');
  });

  it('drops the query and fragment of this edition', () => {
    expect(editionHref(uk, '/category/creatine?page=3&sort=price')).toBe('/uk/category/creatine');
    expect(editionHref(us, '/privacy#retention')).toBe('/privacy');
  });

  // The US address is root-relative with no prefix; from the UK section this
  // is exactly what leaves the /uk app.
  it('never prefixes the US edition', () => {
    expect(us.basePath).toBe('');
    expect(editionHref(us, '/categories')).toBe('/categories');
  });
});

describe('alternateLinksFor', () => {
  const origin = 'https://www.wheyproof.com';
  const both = [us, uk];

  it('links a shared page to itself in every edition, with an x-default', () => {
    expect(alternateLinksFor('/category/creatine', both, origin)).toEqual([
      { hreflang: 'en-US', href: 'https://www.wheyproof.com/category/creatine' },
      { hreflang: 'en-GB', href: 'https://www.wheyproof.com/uk/category/creatine' },
      { hreflang: 'x-default', href: 'https://www.wheyproof.com/category/creatine' },
    ]);
  });

  // Search Console reported the UK guides as duplicates of the US ones while
  // they had no alternates (2026-09-23).
  it('links a guide to the same article in every edition', () => {
    expect(alternateLinksFor('/guides/how-to-choose-whey-protein', both, origin)).toEqual([
      { hreflang: 'en-US', href: 'https://www.wheyproof.com/guides/how-to-choose-whey-protein' },
      { hreflang: 'en-GB', href: 'https://www.wheyproof.com/uk/guides/how-to-choose-whey-protein' },
      { hreflang: 'x-default', href: 'https://www.wheyproof.com/guides/how-to-choose-whey-protein' },
    ]);
  });

  // A product exists in one catalog only; pointing hreflang at the other
  // edition's home page is an error Google reports.
  it('gives edition-specific pages no alternates', () => {
    expect(alternateLinksFor('/product/12/naked-whey', both, origin)).toEqual([]);
    expect(alternateLinksFor('/brand/grenade', both, origin)).toEqual([]);
  });

  it('gives paginated or filtered views no alternates', () => {
    expect(alternateLinksFor('/category/creatine?page=2', both, origin)).toEqual([]);
  });

  // While the UK section is closed it must not be announced anywhere.
  it('gives nothing while only one edition is listed', () => {
    expect(alternateLinksFor('/category/creatine', [us], origin)).toEqual([]);
  });
});
