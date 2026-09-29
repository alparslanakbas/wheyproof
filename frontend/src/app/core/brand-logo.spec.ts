import { describe, expect, it } from 'vitest';

import {
  brandLogoNeedsDarkBackdrop,
  brandLogoUrl,
  brandMonogram,
  brandMonogramColor,
} from './brand-logo';

describe('brandLogoUrl', () => {
  it('points a listed brand to its file, whatever the spelling of its name', () => {
    expect(brandLogoUrl('Optimum Nutrition')).toBe('/brand-logos/optimum-nutrition.webp');
    expect(brandLogoUrl('PER4M')).toBe('/brand-logos/per4m.webp');
    expect(brandLogoUrl('BulkSupplements')).toBe('/brand-logos/bulksupplements.webp');
  });

  // CON-CRET reaches us only through bodybuilding.com: no logo is invented.
  it('returns null for a brand without a downloaded logo', () => {
    expect(brandLogoUrl('CON-CRET')).toBeNull();
    expect(brandLogoUrl('Dymatize')).toBeNull();
  });

  it('gives the white logos a dark circle', () => {
    expect(brandLogoNeedsDarkBackdrop('Veloforte')).toBe(true);
    expect(brandLogoNeedsDarkBackdrop('Optimum Nutrition')).toBe(false);
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
