import { MARKET } from './market';

// The "Lowest since …" badge: today's price is the lowest we have seen since tracking started (Deal.lowestSince; the
// rule lives in the backend's TrackedLow). The badge names the start date because we can't say "all-time low": we
// don't know the price before tracking started.

const ONE_YEAR_MS = 365 * 24 * 60 * 60 * 1000;

/**
 * Card badge: "Lowest since Sep 13" (UK "Lowest since 13 Sept"). Once tracking is over a year old the year is added;
 * without it the badge would point at the most recent Sep 13 and undersell the price.
 */
export function trackedLowLabel(
  iso: string,
  now: Date = new Date(),
  locale: string = MARKET.locale,
  timeZone: string = MARKET.timeZone,
): string {
  const start = new Date(iso);
  const withYear = now.getTime() - start.getTime() >= ONE_YEAR_MS;
  const date = new Intl.DateTimeFormat(locale, {
    month: 'short',
    day: 'numeric',
    ...(withYear ? { year: 'numeric' as const } : {}),
    timeZone,
  }).format(start);
  return `Lowest since ${date}`;
}

/** The day tracking started, in full: "September 13, 2026". */
export function trackedSinceDate(iso: string, locale: string = MARKET.locale, timeZone: string = MARKET.timeZone): string {
  return new Intl.DateTimeFormat(locale, { month: 'long', day: 'numeric', year: 'numeric', timeZone }).format(new Date(iso));
}

/** The badge's tooltip, with the full date. */
export function trackedLowTitle(iso: string): string {
  return `We've tracked this price since ${trackedSinceDate(iso)}; today's price is the lowest we've seen since then.`;
}

/** Both helpers in one field, so a component exposes them to its template with a single line. */
export const TRACKED_LOW = { label: trackedLowLabel, title: trackedLowTitle };
