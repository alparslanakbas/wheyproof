// The market the site is built for. Every price, number and date on the
// site is formatted from these values, so a UK edition changes this one
// place (en-GB / GBP / Europe/London) instead of every template.
export const MARKET = {
  locale: 'en-US',
  currency: 'USD',
  lang: 'en',
  // Fixed rather than the visitor's device zone, so the same scan time reads
  // the same for every visitor.
  timeZone: 'America/New_York',
} as const;

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
