import { MARKET } from './market';

const ABSOLUTE_DATE_FORMATTER = new Intl.DateTimeFormat(MARKET.locale, { month: 'short', day: 'numeric', timeZone: MARKET.timeZone });

function plural(count: number, unit: string): string {
  return `${count} ${unit}${count === 1 ? '' : 's'} ago`;
}

// Plain Date arithmetic, identical on server and client, so safe in SSR. A
// tab left open for a long time shows a stale text (it does not tick), which
// is fine for "when was this last checked".
export function formatRelativeTime(isoDate: string): string {
  const diffMs = Date.now() - new Date(isoDate).getTime();
  const diffMin = Math.floor(diffMs / 60_000);

  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return plural(diffMin, 'minute');

  const diffHour = Math.floor(diffMin / 60);
  if (diffHour < 24) return plural(diffHour, 'hour');

  const diffDay = Math.floor(diffHour / 24);
  if (diffDay === 1) return 'yesterday';
  if (diffDay < 7) return plural(diffDay, 'day');

  return ABSOLUTE_DATE_FORMATTER.format(new Date(isoDate));
}
