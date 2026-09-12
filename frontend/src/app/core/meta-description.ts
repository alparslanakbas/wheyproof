import { formatPrice } from './market';
import { SITE_NAME } from './site-identity';

export interface ProductDescriptionInput {
  displayName: string;
  brandName: string;
  priceText: string;
  discountPercent: number;
  description: string | null | undefined;
}

// Search result description. It opens with a sentence saying WHAT the product
// is, taken from the store's own description when there is one, and adds the
// price after it.
export function buildProductDescription(input: ProductDescriptionInput): string {
  const intro = extractIntro(input.description, input.displayName);

  if (!intro) {
    // Products without a description keep a price template; never empty.
    return input.discountPercent > 0
      ? `${input.displayName} is ${input.priceText} now, a verified ${formatDiscountPercent(input.discountPercent)}% drop at ${input.brandName}. Track its price history on ${SITE_NAME}.`
      : `${input.displayName} costs ${input.priceText} today. Track ${input.brandName} price history on ${SITE_NAME}.`;
  }

  const priceSentence = input.discountPercent > 0
    ? `${input.priceText}, a verified ${formatDiscountPercent(input.discountPercent)}% drop.`
    : `Now ${input.priceText}.`;

  const full = `${intro} ${priceSentence} Price history on ${SITE_NAME}.`;
  // Google cuts descriptions around 160 characters; if it does not fit, the
  // site tail goes first, because what the product is and its price matter more.
  return full.length > 165 ? `${intro} ${priceSentence}` : full;
}

const LEADING_PUNCTUATION = /^[\s:：.,;·–—-]+/;

function extractIntro(raw: string | null | undefined, productName: string): string | null {
  if (!raw) return null;

  // Non-breaking spaces (U+00A0) are common in store copy.
  let text = raw.replace(/ /g, ' ').replace(/\s+/g, ' ').trim();

  // Heading prefixes stores put before the copy.
  text = text.replace(/^(product\s+)?(description|overview|details)\s*[:：]\s*/i, '');
  text = text.replace(/^.{0,60}?what\s+is\s+it\s*\??\s*[:：]\s*/i, '');

  // Some stores write the product name TWICE in a row. The name check below
  // misses it because the spelling differs slightly (®, "2 lb" vs "2lb"). If
  // the opening five-word block appears again, start from the second copy.
  const words = text.split(' ');
  if (words.length >= 10) {
    const block = words.slice(0, 5).join(' ');
    const second = text.indexOf(block, 1);
    if (second > 0) {
      text = text.slice(second).replace(LEADING_PUNCTUATION, '').trim();
    }
  }

  // Copy that repeats the product name loses it: the name is in the title.
  const name = productName.replace(/ /g, ' ').replace(/\s+/g, ' ').trim();
  if (name && text.toLowerCase().startsWith(name.toLowerCase())) {
    const stripped = text.slice(name.length).replace(LEADING_PUNCTUATION, '');
    // Give up if dropping the name cuts the sentence mid-way (the rest would
    // start in lower case).
    if (stripped && stripped[0] === stripped[0].toUpperCase()) {
      text = stripped;
    }
  }

  text = text.replace(LEADING_PUNCTUATION, '').trim();
  if (text.length < 30) return null;

  // First sentence, at least 40 characters so a fragment does not remain.
  const sentence = /^(.{40,}?[.!?])(\s|$)/.exec(text);
  const intro = sentence ? sentence[1] : text;

  if (intro.length <= 120) return intro;
  return intro.slice(0, 117).replace(/\s+\S*$/, '') + '…';
}

/**
 * A title that is not cut in search results.
 *
 * Google cuts titles around 60-70 characters, and rewrites very long titles
 * entirely, so control is lost. Priority: (1) the full product name with the
 * site tail, (2) without the tail, (3) the name trimmed at a word boundary.
 * The product name always leads, since it is what decides the click.
 */
export function buildPageTitle(subject: string, suffix: string, tail: string): string {
  const MAX = 65;
  const full = `${subject} ${suffix} | ${tail}`;
  if (full.length <= MAX) return full;

  const withoutTail = `${subject} ${suffix}`;
  if (withoutTail.length <= MAX) return withoutTail;

  const room = MAX - suffix.length - 2;
  return `${trimAtWordBoundary(subject, Math.max(20, room))} ${suffix}`;
}

/**
 * Meaningless fragments LEFT AT THE END after a cut. Cutting at a word
 * boundary is not enough: "…Quadro Whey 366…" leaves a number torn from its
 * unit, "…Bar 45g x…" an orphan letter. Only clear fragments go: a run of
 * digits, a single letter or a joining sign. "45g" (with a letter) stays.
 */
const ORPHAN_FRAGMENT = /(?:\s+[\d.,]+|\s+[a-zA-Z]|\s+[x×+/&–-])+$/;

/** A trailing separator, which makes a title look broken ("2026 |…"). */
const TRAILING_SEPARATOR = /[\s|·,:;&/–—-]+$/;

/**
 * Brings text under `max` characters without cutting a word and without a
 * trailing fragment or separator; adds "…" only when it really cut.
 */
function trimAtWordBoundary(text: string, max: number): string {
  if (text.length <= max) return text;

  const trimmed = text
    .slice(0, max)
    .replace(/\s+\S*$/, '')
    .replace(ORPHAN_FRAGMENT, '')
    .replace(TRAILING_SEPARATOR, '');

  // Everything was removed (one very long word): fall back to a hard cut.
  return trimmed ? `${trimmed}…` : `${text.slice(0, max)}…`;
}

/** Keeps a meta description under Google's cut-off. */
export function clampDescription(text: string, max = 155): string {
  if (text.length <= max) return text;
  return text.slice(0, max).replace(/\s+\S*$/, '') + '…';
}

/**
 * Keeps a title short enough not to be cut in search results.
 *
 * The last safety net: `page-meta.service.ts` applies it to EVERY page,
 * including brand and category titles that do not go through buildPageTitle.
 * When the tail has to go, it goes whole: a title ending "…Deals 2026" beats
 * one ending "…Deals 2026 |…". Losing the site name is better than leaving
 * half a separator.
 */
export function clampTitle(text: string, max = 65): string {
  if (text.length <= max) return text;

  const separator = text.lastIndexOf(' | ');
  if (separator > 0) {
    const withoutTail = text.slice(0, separator);
    if (withoutTail.length <= max) return withoutTail;
    return trimAtWordBoundary(withoutTail, max);
  }

  return trimAtWordBoundary(text, max);
}

// --- Shared helpers -------------------------------------------------------

/**
 * Discount percentages in meta text are WHOLE numbers. The raw value has
 * decimals, and "30.8%" in a search snippet is needless precision.
 */
export function formatDiscountPercent(percent: number): string {
  return String(Math.round(percent));
}

/** Price as written in meta text, in the site's market currency. */
export function formatPriceText(price: number): string {
  return formatPrice(price);
}

// --- Review page description ----------------------------------------------

/**
 * Minimum history before a number of days is WORTH mentioning.
 *
 * On a young catalog most products have a day or two of prices, so "the
 * lowest price in 30 days" is technically TRUE for almost every product and
 * means nothing. This site exists to expose empty discount claims, so the
 * day count is only mentioned when it is real.
 */
const MEANINGFUL_HISTORY_DAYS = 14;

/**
 * Minimum history before calling a discount "verified". A separate, lower
 * threshold: a discount is an observed drop, not a range. Still, two days of
 * data is more than the word can carry. Below it, no discount is CLAIMED,
 * only the price is given.
 */
const DAYS_NEEDED_FOR_DISCOUNT = 7;

export interface ReviewDescriptionInput {
  displayName: string;
  priceText: string;
  discountPercent: number;
  /** Number of DISTINCT days with a price point (repeats within a day don't count). */
  historyDays: number;
}

/**
 * Search result description for the review page. Every review page once had
 * the same sentence with no concrete number; a searcher types the product
 * name and wants the price, so the price leads.
 */
export function buildReviewDescription(input: ReviewDescriptionInput): string {
  const meaningfulHistory = input.historyDays >= MEANINGFUL_HISTORY_DAYS;
  const canClaimDiscount =
    input.discountPercent > 0 && input.historyDays >= DAYS_NEEDED_FOR_DISCOUNT;

  const tail = meaningfulHistory
    ? `Independent review with ${input.historyDays} days of price history, nutrition facts and a category comparison.`
    : 'Independent review with nutrition facts and a category comparison.';

  const head = canClaimDiscount
    ? `${input.displayName} is ${input.priceText}, a verified ${formatDiscountPercent(input.discountPercent)}% drop by our own price history, not the store's label.`
    : `${input.displayName} costs ${input.priceText} today.`;

  return clampDescription(`${head} ${tail}`);
}
