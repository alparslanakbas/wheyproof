import { MARKET } from './market';

const GRAMS_PER_POUND = 453.59237;

/**
 * The unit the "Which supplement?" lists compare prices in: per pound in the
 * US edition (tubs are sold in pounds), per kilogram in the UK. The API always
 * returns a price per kilogram; this is only the display conversion.
 */
export const PRICE_UNIT =
  MARKET.measurement === 'imperial'
    ? { label: 'lb', word: 'pound', perKg: GRAMS_PER_POUND / 1000 }
    : { label: 'kg', word: 'kilogram', perKg: 1 };

export function unitPrice(pricePerKg: number): number {
  return pricePerKg * PRICE_UNIT.perKg;
}
