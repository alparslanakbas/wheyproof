import { describe, expect, it } from 'vitest';

import { brandLogoUrl, brandMonogram, brandMonogramColor } from './brand-logo';

describe('brandLogoUrl', () => {
  // No US brand logo has been downloaded yet, and none is invented: every
  // brand shows its monogram until a verified logo file is added.
  it('returns null for brands without a downloaded logo', () => {
    expect(brandLogoUrl('Optimum Nutrition')).toBeNull();
    expect(brandLogoUrl('Transparent Labs')).toBeNull();
    expect(brandLogoUrl('Nutricost')).toBeNull();
  });
});

describe('brandMonogram', () => {
  it('takes the two initials of a two-word name', () => {
    expect(brandMonogram('Transparent Labs')).toBe('TL');
    expect(brandMonogram('Optimum Nutrition')).toBe('ON');
  });

  it('takes the first two letters of a one-word name', () => {
    expect(brandMonogram('Kaged')).toBe('KA');
    expect(brandMonogram('Ghost')).toBe('GH');
  });

  // Locale-independent upper-casing: "i" becomes "I", never a dotted "İ".
  it('upper-cases the English way', () => {
    expect(brandMonogram('ironMaxx')).toBe('IR');
    expect(brandMonogram('Sixpack')).toBe('SI');
  });

  it('treats hyphens and dots as word boundaries', () => {
    expect(brandMonogram('Z-Konzept')).toBe('ZK');
    expect(brandMonogram('Sci-Tech')).toBe('ST');
  });

  it("doesn't fail on an empty name", () => {
    expect(brandMonogram('')).toBe('?');
    expect(brandMonogram('   ')).toBe('?');
  });
});

describe('brandMonogramColor', () => {
  // NOT random: a brand must get the same color every time, or people can't
  // recognise it by color.
  it('always gives the same brand the same color', () => {
    expect(brandMonogramColor('Kaged')).toBe(brandMonogramColor('Kaged'));
    expect(brandMonogramColor('Dymatize')).toBe(brandMonogramColor('Dymatize'));
  });

  it('returns a valid hex color', () => {
    for (const name of ['Kaged', 'Dymatize', 'Ghost', 'Nutricost', 'Quest', '']) {
      expect(brandMonogramColor(name)).toMatch(/^#[0-9a-f]{6}$/);
    }
  });
});
