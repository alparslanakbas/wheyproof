import { describe, expect, it } from 'vitest';

import { blackFriday, cyberMonday } from './campaign';

const day = (d: Date) => d.toISOString().slice(0, 10);

describe('blackFriday', () => {
  it('is the day after the fourth Thursday of November', () => {
    expect(day(blackFriday(2026))).toBe('2026-11-27');
    expect(day(blackFriday(2027))).toBe('2027-11-26');
    expect(day(blackFriday(2025))).toBe('2025-11-28');
  });

  it('differs from the last Friday of November when November has five Thursdays', () => {
    expect(day(blackFriday(2029))).toBe('2029-11-23');
  });
});

describe('cyberMonday', () => {
  it('is the Monday after Black Friday, even across the month end', () => {
    expect(day(cyberMonday(2026))).toBe('2026-11-30');
    expect(day(cyberMonday(2025))).toBe('2025-12-01');
  });
});
