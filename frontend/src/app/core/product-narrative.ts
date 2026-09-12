import { Deal } from './deal.model';
import { displayName } from './display-name';
import { MARKET, formatPrice } from './market';
import {
  PROTEIN_REFERENCE_GRAMS,
  pricePerServing,
  proteinRatioPercent,
  proteinReferenceCost,
  servingsInPackage,
} from './value-metrics';

/**
 * The product page narrative, written from our own measurements.
 *
 * WHY: on the Turkish site Search Console left part of the product pages out
 * of the index ("Crawled - currently not indexed"). Measured: the pages were
 * 47.7% alike at ~300 words. Once the store's copy was removed (duplicate
 * content), only a templated label/value list was left and the numbers were
 * the only thing that changed.
 *
 * These sentences change their STRUCTURE with the data, not just the
 * numbers: a product whose price never moved and one at its 30-day low get
 * entirely different sentences, so every page really differs.
 *
 * Everything is our own measurement; no word is copied from the store, and
 * no sentence is written for anything we can't calculate.
 */

const ratingFormatter = new Intl.NumberFormat(MARKET.locale, {
  minimumFractionDigits: 1,
  maximumFractionDigits: 2,
});

/** The price story; entirely different sentences depending on the history. */
function priceParagraph(deal: Deal, discountEventCount?: number): string {
  const name = displayName(deal.productName);
  const sentences: string[] = [];

  if (deal.isAtThirtyDayLow) {
    sentences.push(
      `${name} is ${formatPrice(deal.currentPrice)} right now, the lowest price we measured in the last 30 days.`,
    );
    sentences.push(
      `In the same period we also saw ${formatPrice(deal.referencePrice)}; today's price is ${deal.discountPercent}% below that.`,
    );
  } else if (deal.discountPercent > 0) {
    sentences.push(
      `${name} is ${formatPrice(deal.currentPrice)} right now. The highest price we measured in the last 30 days was ${formatPrice(deal.referencePrice)}, so this is ${deal.discountPercent}% below the reference.`,
    );
  } else {
    sentences.push(
      `${name} is ${formatPrice(deal.currentPrice)} right now, the same as the reference price we measured over the last 30 days, so there is no drop at the moment.`,
    );
  }

  if (discountEventCount !== undefined && discountEventCount > 0) {
    sentences.push(
      discountEventCount === 1
        ? 'Its price dropped once in the period we tracked.'
        : `Its price dropped ${discountEventCount} times in the period we tracked.`,
    );
  }

  // The store's own "was" label is kept apart from our measurement: the
  // site's whole value rests on that distinction.
  const store = deal.seller ?? deal.brandName;
  if (deal.storeOldPrice !== null && deal.storeDiscountPercent !== null) {
    sentences.push(
      `${store} shows a "was" price of ${formatPrice(deal.storeOldPrice)} on its own site (${deal.storeDiscountPercent}% off). That is the store's claim; the drop we verify is based on the reference above.`,
    );
  }

  return sentences.join(' ');
}

/** Package economics, only with real serving data. */
function economicsParagraph(deal: Deal): string | null {
  const servings = servingsInPackage(deal);
  const perServing = pricePerServing(deal);
  if (servings === null || perServing === null) return null;

  const sentences: string[] = [];
  const source =
    deal.servingsPerPackage !== null
      ? `${deal.brandName} states ${Math.round(servings)} servings per package`
      : `A ${deal.size} package with ${deal.servingSizeGrams} g servings gives about ${Math.round(servings)} servings`;
  sentences.push(`${source}, which works out to ${formatPrice(perServing)} per serving.`);

  const ratio = proteinRatioPercent(deal);
  const refCost = proteinReferenceCost(deal);
  if (deal.proteinPerServingGrams !== null && ratio !== null) {
    sentences.push(
      `Each serving has ${deal.proteinPerServingGrams} g of protein, meaning ${ratio}% of the serving is protein.`,
    );
    if (refCost !== null) {
      sentences.push(
        `Getting ${PROTEIN_REFERENCE_GRAMS} g of protein from this product costs ${formatPrice(refCost)}.`,
      );
    }
  }

  return sentences.join(' ');
}

/** The store's own customer rating, stated plainly as not ours. */
function ratingParagraph(deal: Deal): string | null {
  if (deal.ratingValue === null || deal.ratingCount === null) return null;
  // A long disclaimer repeated word for word on every product made pages
  // alike; the full explanation is on /how-it-works. Here only the source.
  return (
    `On its own site, ${deal.seller ?? deal.brandName} shows an average of ` +
    `${ratingFormatter.format(deal.ratingValue)} out of 5 from ${deal.ratingCount} reviews (the store's customer rating, not ours).`
  );
}

/** We also say what we could not measure; missing data is not hidden. */
function limitationsParagraph(deal: Deal): string | null {
  const missing: string[] = [];
  if (deal.servingSizeGrams === null && deal.servingsPerPackage === null) {
    missing.push('the serving size');
  }
  if (!deal.nutritionJson) missing.push('a nutrition facts table');
  if (missing.length === 0) return null;

  return (
    `${deal.brandName} doesn't publish ${missing.join(' or ')} for this product in a form we can read. ` +
    "That's why the related calculations are missing here: we leave the field empty rather than estimate it."
  );
}

/**
 * A sentence built from the nutrition table. It is the data that varies most
 * between products (every table has different rows), so it does the most to
 * tell pages apart.
 */
function nutritionParagraph(deal: Deal): string | null {
  if (!deal.nutritionJson) return null;

  let rows: Record<string, string>;
  try {
    rows = JSON.parse(deal.nutritionJson) as Record<string, string>;
  } catch {
    return null;
  }

  const entries = Object.entries(rows).filter(([k, v]) => k && v);
  if (entries.length === 0) return null;

  const listed = entries.slice(0, 6).map(([k, v]) => `${k.toLowerCase()} ${v}`);
  const portion = deal.servingSizeGrams !== null ? `a ${deal.servingSizeGrams} g serving` : 'each serving';
  const extra = entries.length > 6 ? ` The table has ${entries.length} rows in total.` : '';

  return `According to the ${deal.brandName} label, ${portion} contains ${listed.join(', ')}.${extra}`;
}

export function buildProductNarrative(deal: Deal, discountEventCount?: number): string[] {
  const paragraphs: (string | null)[] = [
    priceParagraph(deal, discountEventCount),
    economicsParagraph(deal),
    nutritionParagraph(deal),
    ratingParagraph(deal),
    limitationsParagraph(deal),
    // No generic closing sentence on purpose: identical on every product, it
    // only made pages look alike.
  ];

  return paragraphs.filter((p): p is string => p !== null);
}
