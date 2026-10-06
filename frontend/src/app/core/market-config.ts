// The market the site is built for. Every price, number and date on the
// site is formatted from these values (see market.ts). The UK build swaps this
// file for market-config.uk.ts (angular.json "uk"); only VALUES live here, so
// the two editions share one formatting code path.
export const MARKET = {
  locale: 'en-US',
  currency: 'USD',
  // Written into <html lang> (app.ts).
  lang: 'en',
  // Names the market where a page must say which one it covers (the home page title and
  // description); null on the US edition, whose texts stay as they are.
  regionAdjective: null as string | null,
  // Fixed rather than the visitor's device zone, so the same scan time reads
  // the same for every visitor.
  timeZone: 'America/New_York',
  // Which units the calculators open with. US visitors weigh themselves in
  // pounds and feet; the UK section opens in kilograms and centimetres.
  measurement: 'imperial' as 'imperial' | 'metric',
} as const;
