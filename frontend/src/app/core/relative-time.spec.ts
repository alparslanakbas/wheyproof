import { formatRelativeTime } from './relative-time';

describe('formatRelativeTime', () => {
  const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString();
  const hoursAgo = (h: number) => new Date(Date.now() - h * 60 * 60_000).toISOString();
  const daysAgo = (d: number) => new Date(Date.now() - d * 24 * 60 * 60_000).toISOString();

  it('returns "just now" under a minute', () => {
    expect(formatRelativeTime(new Date().toISOString())).toBe('just now');
  });

  it('shows minutes', () => {
    expect(formatRelativeTime(minutesAgo(5))).toBe('5 minutes ago');
    expect(formatRelativeTime(minutesAgo(1))).toBe('1 minute ago');
  });

  it('shows hours', () => {
    expect(formatRelativeTime(hoursAgo(3))).toBe('3 hours ago');
  });

  it('returns "yesterday" for exactly one day', () => {
    expect(formatRelativeTime(daysAgo(1))).toBe('yesterday');
  });

  it('returns "X days ago" for 2-6 days', () => {
    expect(formatRelativeTime(daysAgo(3))).toBe('3 days ago');
  });

  it('falls back to an absolute date (month and day) after 7 days', () => {
    const result = formatRelativeTime(daysAgo(10));

    expect(result).not.toContain('ago');
    expect(result).not.toBe('yesterday');
  });
});
