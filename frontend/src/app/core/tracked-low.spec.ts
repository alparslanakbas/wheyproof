import { describe, expect, it } from 'vitest';

import { trackedLowLabel, trackedSinceDate } from './tracked-low';

const NOW = new Date('2026-10-20T19:00:00Z');

describe('trackedLowLabel', () => {
  it('names the start day in each edition’s own format', () => {
    expect(trackedLowLabel('2026-09-13T12:00:00Z', NOW, 'en-US', 'America/New_York')).toBe('Lowest since Sep 13');
    expect(trackedLowLabel('2026-09-13T12:00:00Z', NOW, 'en-GB', 'Europe/London')).toMatch(/^Lowest since 13 Sept?$/);
  });

  it('uses the edition’s time zone, not UTC', () => {
    // 02:00 UTC on Sep 13 is still Sep 12 in New York.
    expect(trackedLowLabel('2026-09-13T02:00:00Z', NOW, 'en-US', 'America/New_York')).toBe('Lowest since Sep 12');
  });

  it('adds the year once tracking is over a year old', () => {
    const later = new Date('2027-10-01T12:00:00Z');
    expect(trackedLowLabel('2026-09-13T12:00:00Z', later, 'en-US', 'America/New_York')).toBe('Lowest since Sep 13, 2026');
  });
});

describe('trackedSinceDate', () => {
  it('writes the full date', () => {
    expect(trackedSinceDate('2026-09-13T12:00:00Z', 'en-US', 'America/New_York')).toBe('September 13, 2026');
  });
});
