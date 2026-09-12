import { brandSlug } from './brand-slug';

/**
 * Brands whose logo has been downloaded to `frontend/public/brand-logos/`.
 *
 * <b>WHY SELF-HOSTED:</b> hotlinking a brand's own CDN has two problems:
 * 1. An unreachable host does NOT fire `onerror`; the request hangs, the
 *    fallback never appears and the card stays empty indefinitely.
 * 2. When a brand changes or moves its logo, the card empties silently.
 * Files are 128px WebP.
 *
 * <b>THE LIST IS KEPT BY HAND, NOT GENERATED:</b> only brands whose own
 * address really is theirs get a logo. Brands that reach us only through a
 * retailer carry the retailer's address; pulling a favicon from there would
 * put the retailer's logo on them. Those brands get a monogram, never an
 * invented logo.
 *
 * Empty until US brand logos are added; every brand shows its monogram.
 */
const BRANDS_WITH_LOCAL_LOGO: ReadonlySet<string> = new Set<string>([]);

/**
 * Light logos on a transparent background, which vanish on the card's light
 * grey circle. Chosen by MEASURING each file (mean brightness of opaque
 * pixels > 200 and transparency > 20%), not by eye.
 */
const NEEDS_DARK_BACKDROP: ReadonlySet<string> = new Set<string>([]);

/** Path to the logo file, or null. */
export function brandLogoUrl(brandName: string): string | null {
  const slug = brandSlug(brandName);
  return BRANDS_WITH_LOCAL_LOGO.has(slug) ? `/brand-logos/${slug}.webp` : null;
}

/** A light, transparent logo needs a darker circle behind it. */
export function brandLogoNeedsDarkBackdrop(brandName: string): boolean {
  return NEEDS_DARK_BACKDROP.has(brandSlug(brandName));
}

/**
 * Monogram letters for brands without a logo.
 *
 * A generic store icon repeated on most brands made the directory look
 * incomplete and the brands indistinguishable. A monogram is not invented
 * information: it comes from the brand's own name. Two words give their
 * initials ("Transparent Labs" -> "TL"), one word its first two letters
 * ("Kaged" -> "KA").
 */
export function brandMonogram(brandName: string): string {
  const words = brandName
    .split(/[\s.&-]+/)
    .filter((w) => /[\p{L}0-9]/u.test(w));

  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();

  return (words[0][0] + words[1][0]).toUpperCase();
}

// Monogram backgrounds: dark tones from the site palette, checked for
// contrast with white text.
const MONOGRAM_COLORS = [
  '#4c4bb8',
  '#2f6f5e',
  '#8a4b7d',
  '#a15c2f',
  '#3a6b96',
  '#7a3f52',
  '#4d6b2f',
  '#6b4a94',
];

/**
 * A FIXED background color from the brand name: the same brand always gets
 * the same color (not random), so people can recognise it by color too.
 */
export function brandMonogramColor(brandName: string): string {
  let hash = 0;
  for (let i = 0; i < brandName.length; i++) {
    hash = (hash * 31 + brandName.charCodeAt(i)) >>> 0;
  }
  return MONOGRAM_COLORS[hash % MONOGRAM_COLORS.length];
}
