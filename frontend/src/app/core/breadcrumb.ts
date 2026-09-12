import { canonicalOrigin } from './canonical-link';

export interface BreadcrumbItem {
  name: string;
  path: string;
}

// schema.org BreadcrumbList, shared by category, brand, product and guide
// pages. Search results can show "Home › Category › Product" instead of a
// bare URL.
export function buildBreadcrumbJsonLd(document: Document, items: BreadcrumbItem[]): object {
  const origin = canonicalOrigin(document);
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: items.map((item, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: item.name,
      item: `${origin}${item.path}`,
    })),
  };
}
