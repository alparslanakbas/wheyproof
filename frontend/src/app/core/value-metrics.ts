import { Deal } from './deal.model';
import { packageGrams } from './package-size';

// "It's on sale, but is it actually good value?"
//
// A price per serving is not enough: it hides how much of each serving is the
// active ingredient. The data is already collected (serving size and protein
// per serving from the brand's nutrition label); these helpers present it.
//
// RULE: no data, no number. Some comparison sites assume a standard 30 g
// serving when they don't know it; we never assume, we show nothing.

// Fixed protein amount to compare products on. Package and serving sizes
// differ from brand to brand, so products only line up on a common unit.
// 30 g matches the reference already used on the product review page; two
// different references on two pages would mislead.
export const PROTEIN_REFERENCE_GRAMS = 30;

/**
 * Servings in the package. Same order as the backend's `CalculateServings`:
 * the brand's own statement first, otherwise package weight ÷ serving size.
 */
export function servingsInPackage(deal: Deal): number | null {
  if (deal.servingsPerPackage && deal.servingsPerPackage > 0) return deal.servingsPerPackage;

  const grams = packageGrams(deal.size);
  if (grams && deal.servingSizeGrams && deal.servingSizeGrams > 0) {
    return grams / deal.servingSizeGrams;
  }
  return null;
}

/** Cost of one serving. */
export function pricePerServing(deal: Deal): number | null {
  const servings = servingsInPackage(deal);
  if (!servings || servings < 1) return null;
  return deal.currentPrice / servings;
}

/**
 * What share of a serving is protein: "how much of what you pay goes to the
 * active ingredient". 24 g of protein in a 30 g serving is 80%; 20 g is 66%.
 */
export function proteinRatioPercent(deal: Deal): number | null {
  const { proteinPerServingGrams: protein, servingSizeGrams: serving } = deal;
  if (!protein || !serving || serving <= 0) return null;

  const ratio = (protein / serving) * 100;
  // Inconsistent label data (more protein than the serving) is not shown.
  if (ratio <= 0 || ratio > 100) return null;
  return Math.round(ratio);
}

/**
 * Cost of a fixed amount of protein (PROTEIN_REFERENCE_GRAMS). It is free of
 * package and serving size differences, so it is the fairest way to compare
 * two products directly.
 */
export function proteinReferenceCost(deal: Deal): number | null {
  const servings = servingsInPackage(deal);
  const protein = deal.proteinPerServingGrams;
  if (!servings || servings < 1 || !protein || protein <= 0) return null;

  const totalProtein = servings * protein;
  if (totalProtein <= 0) return null;

  return (deal.currentPrice / totalProtein) * PROTEIN_REFERENCE_GRAMS;
}
