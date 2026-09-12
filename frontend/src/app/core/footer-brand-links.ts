/**
 * Should the footer's brand link list show on this page?
 *
 * NOT on PRODUCT and REVIEW pages. Measured on the Turkish site: a product
 * page was 612 words, the footer 334 of them (54%), and 241 (39% of the page)
 * were the brand list alone, against 34-86 words about the product. The same
 * block repeated on thousands of pages, which is a known cause of "Crawled -
 * currently not indexed". Removing it roughly doubles the product-specific
 * share of the page.
 *
 * The list stays on the home, brand and category pages, so Google loses no
 * way into the brand pages. The /brands directory already lists every brand
 * in its main content, so the footer does not repeat it there.
 */
export function showFooterBrandLinks(path: string): boolean {
  return path !== '/brands' && !path.startsWith('/product/') && !path.startsWith('/review/');
}
