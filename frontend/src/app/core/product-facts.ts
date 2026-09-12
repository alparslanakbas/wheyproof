import { Deal } from './deal.model';
import { CATEGORY_LABELS } from './category-labels';
import { MARKET, formatPrice } from './market';
import { SITE_NAME } from './site-identity';
import {
  PROTEIN_REFERENCE_GRAMS,
  pricePerServing,
  proteinRatioPercent,
  proteinReferenceCost,
  servingsInPackage,
} from './value-metrics';

/**
 * The "what we measured" list on product pages.
 *
 * Why: the store's own description used to be published here word for word.
 * We still COLLECT it (serving and nutrition extraction rely on it) but no
 * longer show it: republishing someone else's marketing copy is content that
 * isn't ours and a pattern search engines treat as duplicate content.
 *
 * This list comes entirely from our own data: price history, servings per
 * package, cost per serving, protein density. Nothing is estimated; a value
 * that can't be calculated produces no row at all.
 */
export interface ProductFact {
  label: string;
  value: string;
}

const ratingFormatter = new Intl.NumberFormat(MARKET.locale, {
  minimumFractionDigits: 1,
  maximumFractionDigits: 2,
});

/**
 * @param discountEventCount Real price drops measured in the selected period.
 *   Not given where no price history is loaded (cards, lists).
 */
export function buildProductFacts(deal: Deal, discountEventCount?: number): ProductFact[] {
  const facts: ProductFact[] = [];

  if (deal.category) {
    facts.push({
      label: 'Category',
      value: CATEGORY_LABELS[deal.category] ?? deal.category,
    });
  }

  if (deal.size) {
    facts.push({ label: 'Package', value: deal.size });
  }

  const servings = servingsInPackage(deal);
  if (servings !== null) {
    const source =
      deal.servingsPerPackage !== null
        ? `stated by ${deal.brandName}`
        : `${deal.size} ÷ ${deal.servingSizeGrams} g serving`;
    // Package weight ÷ serving is rarely a whole number (5 lb ÷ 31 g); it is
    // rounded and called "about" instead of printing the raw decimal.
    const isExact = Number.isInteger(servings);
    facts.push({
      label: 'Servings per package',
      value: `${isExact ? '' : 'about '}${Math.round(servings)} servings (${source})`,
    });
  }

  if (deal.servingSizeGrams !== null) {
    facts.push({ label: 'Serving size', value: `${deal.servingSizeGrams} g` });
  }

  const perServing = pricePerServing(deal);
  if (perServing !== null) {
    facts.push({ label: 'Cost per serving', value: formatPrice(perServing) });
  }

  if (deal.proteinPerServingGrams !== null) {
    facts.push({
      label: 'Protein per serving',
      value: `${deal.proteinPerServingGrams} g`,
    });
  }

  const ratio = proteinRatioPercent(deal);
  if (ratio !== null) {
    facts.push({
      label: 'Protein density',
      value: `${ratio}% (how much of a serving is protein)`,
    });
  }

  const referenceCost = proteinReferenceCost(deal);
  if (referenceCost !== null) {
    facts.push({
      label: `Cost of ${PROTEIN_REFERENCE_GRAMS} g of protein`,
      value: formatPrice(referenceCost),
    });
  }

  facts.push({
    label: 'Reference price we measured',
    value:
      deal.referencePrice > deal.currentPrice
        ? `${formatPrice(deal.referencePrice)} — the current price is ${deal.discountPercent}% below it`
        : `${formatPrice(deal.referencePrice)} — the current price is at the reference level`,
  });

  if (deal.isAtThirtyDayLow) {
    facts.push({
      label: '30-day trend',
      value: 'The current price is the lowest we measured in the last 30 days',
    });
  }

  if (discountEventCount !== undefined && discountEventCount > 0) {
    facts.push({
      label: 'Price drops measured',
      value: discountEventCount === 1 ? 'once' : `${discountEventCount} times`,
    });
  }

  // The customer rating on the store's own site. Not our measurement, so the
  // label starts with the store's name, in the same "the store's claim" group
  // as the store sale row below.
  if (deal.ratingValue !== null && deal.ratingCount !== null) {
    facts.push({
      label: `Customer rating on ${deal.seller ?? deal.brandName}`,
      value: `${ratingFormatter.format(deal.ratingValue)} out of 5 (${deal.ratingCount} reviews)`,
    });
  }

  if (deal.storeOldPrice !== null && deal.storeDiscountPercent !== null) {
    facts.push({
      label: `What ${deal.seller ?? deal.brandName} shows on its own site`,
      value: `Was ${formatPrice(deal.storeOldPrice)}, ${deal.storeDiscountPercent}% off (the store's claim, not our verification)`,
    });
  }

  if (deal.flavor) {
    facts.push({ label: 'Flavor', value: deal.flavor });
  }

  // The seller is only set for retailer sources. On the brand's own store it
  // is null and no row is produced; "Seller: Nutricost" would be noise.
  if (deal.seller) {
    facts.push({
      label: 'Seller',
      value: `A ${deal.brandName} product sold by ${deal.seller}.`,
    });
  }

  // Only for sources that REPORT stock. `=== false` is required: null means
  // "this source doesn't report stock", not "out of stock".
  //
  // An out-of-stock product stays in the list so its price history continues,
  // but it is said plainly here so nobody visits the store for nothing.
  if (deal.inStock === false) {
    // Stock runs out at the SELLER's store. For a retailer listing, the brand
    // may still have it on its own site.
    const store = deal.seller ?? deal.brandName;
    facts.push({
      label: 'Stock',
      value: `Out of stock on ${store} at our last check. We keep tracking its price.`,
    });
  }

  return facts;
}

/**
 * The product page's schema.org `description`. Built from our own
 * measurements, NOT the store's copy (see above). It differs per product
 * because the numbers do.
 */
export function buildProductJsonLdDescription(deal: Deal): string {
  const parts: string[] = [];

  const category = deal.category ? (CATEGORY_LABELS[deal.category] ?? deal.category) : null;
  parts.push(
    category
      ? `A ${category.toLowerCase()} product by ${deal.brandName}.`
      : `A product by ${deal.brandName}.`,
  );

  if (deal.size) parts.push(`Package: ${deal.size}.`);
  if (deal.servingSizeGrams !== null) parts.push(`Serving size: ${deal.servingSizeGrams} g.`);
  if (deal.proteinPerServingGrams !== null) {
    parts.push(`${deal.proteinPerServingGrams} g of protein per serving.`);
  }

  const perServing = pricePerServing(deal);
  if (perServing !== null) parts.push(`${formatPrice(perServing)} per serving.`);

  parts.push(`Price history is measured regularly by ${SITE_NAME}.`);

  return parts.join(' ');
}
