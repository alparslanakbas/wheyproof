// Grams per unit, the exact international avoirdupois definitions. Must match
// the backend's ProductAttributeParser.ToGrams: US tubs are sold in pounds,
// and a different factor here would skew every per-serving price silently.
const GRAMS_PER_UNIT: Record<string, number> = {
  g: 1,
  kg: 1000,
  lb: 453.59237,
  oz: 28.349523125,
};

/**
 * Grams in a package size as the API writes it ("5 lb", "250 g", "1.5 kg").
 * Null for counts ("30 servings", "120 capsules"), where a weight would be
 * invented.
 */
export function packageGrams(size: string | null | undefined): number | null {
  if (!size) return null;
  const match = /^(\d+(?:\.\d+)?)\s*([a-z]+)$/i.exec(size.trim());
  if (!match) return null;

  const gramsPerUnit = GRAMS_PER_UNIT[match[2].toLowerCase()];
  const value = Number(match[1]);
  return gramsPerUnit && value > 0 ? value * gramsPerUnit : null;
}
