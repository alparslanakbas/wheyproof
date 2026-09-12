// Raw product names differ by store: some are ALL CAPS ("GHOST® WHEY |
// CHOCOLATE CHIP COOKIES"), most are already Title Case. An ALL CAPS title,
// H1 or search snippet reads poorly and looks like spam. This is used ONLY
// for display text (title, description, H1, card title, JSON-LD name); URL
// slugs still come from the original productName through slugify.

// Tokens kept in capitals. Units stay as the store wrote them.
const ACRONYMS = new Set([
  'BCAA', 'EAA', 'EAAS', 'BCAAS', 'HMB', 'MCT', 'CLA', 'ZMA', 'NAD', 'NMN', 'DHEA',
  'TUDCA', 'MSM', 'GABA', 'KSM', 'USP', 'NSF', 'WPC', 'WPI', 'WPH', 'ISO',
  'HTP', 'AMP', 'GNC', 'SSN', 'RAW', 'NOX', 'PRE', 'MCG', 'IU', 'USA', 'US',
  'LB', 'LBS', 'OZ', 'ML', 'KG', 'CT', 'XL', 'XXL', 'PM', 'AM', 'DNA', 'EPA', 'DHA',
]);

// Words that stay lowercase inside a title ("CREAM OF RICE" → "Cream of
// Rice"). "a", "in" and "on" are left out on purpose: they would turn
// "VITAMIN A" into "Vitamin a", and "ON" can be a brand.
const LOWERCASE_WORDS = new Set(['of', 'the', 'and', 'for', 'with', 'or', 'to']);

function formatWord(word: string): string {
  const upper = word.toUpperCase();

  // Not entirely upper case (lower or mixed): leave it alone. This keeps the
  // function idempotent and keeps readable spellings like "g" or "ml".
  if (word !== upper) return word;

  const lower = word.toLowerCase();
  if (LOWERCASE_WORDS.has(lower)) return lower;
  if (ACRONYMS.has(upper)) return upper;

  // Short (1-2 letters), all caps and unknown: probably a unit or code not in
  // the list. Leave it rather than guess.
  if (word.length <= 2) return word;

  return lower.charAt(0).toUpperCase() + lower.slice(1);
}

export function displayName(raw: string): string {
  if (!raw) return raw;
  // \p{L}+ only touches runs of letters; numbers, parentheses, dashes and
  // other punctuation are left as they are ("240g", "(12 x 14 fl. oz.)").
  return raw.replace(/\p{L}+/gu, formatWord);
}
