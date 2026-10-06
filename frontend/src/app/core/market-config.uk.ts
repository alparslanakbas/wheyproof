// The UK section's market values (replaces market-config.ts in the "uk" build).
export const MARKET = {
  locale: 'en-GB',
  currency: 'GBP',
  // en-GB, not en: the page is British English (SEO audit 2026-10-06, item 12; Google targets
  // by hreflang, but the attribute should still be right).
  lang: 'en-GB',
  // The UK home page had the US title and description word for word; search results never
  // said the page was the UK edition (same audit item).
  regionAdjective: 'UK' as string | null,
  timeZone: 'Europe/London',
  measurement: 'metric' as 'imperial' | 'metric',
} as const;
