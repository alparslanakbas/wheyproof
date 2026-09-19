import { MARKET } from './market-config';

// Values come from market-config.ts, which the UK build replaces; the
// formatting below is shared by both editions.
export { MARKET };

const priceFormatter = new Intl.NumberFormat(MARKET.locale, {
  style: 'currency',
  currency: MARKET.currency,
});

const wholePriceFormatter = new Intl.NumberFormat(MARKET.locale, {
  style: 'currency',
  currency: MARKET.currency,
  maximumFractionDigits: 0,
});

/** "$1,234.56" */
export function formatPrice(value: number): string {
  return priceFormatter.format(value);
}

/** "$1,235" — for summaries where cents are noise. */
export function formatWholePrice(value: number): string {
  return wholePriceFormatter.format(value);
}
