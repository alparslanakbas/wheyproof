// Prepares search box text for comparison.
//
// Accents are stripped (NFD, then combining marks removed) so "acai" finds
// "Açaí" and vice versa. Dotless "ı" does not decompose under NFD, so it is
// mapped by hand. Lower-casing uses the culture-independent toLowerCase():
// a locale-aware one turned "I" into "ı" under tr-TR on the Turkish site, and
// "hiq" stopped matching "HIQ".
//
// slugify can't be reused: it cuts at 80 characters (right for a URL), while
// the searchable text here is a brand name plus all its category labels and
// easily longer; the trailing categories would become unsearchable.
export function normalizeSearchText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/ı/g, 'i')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Does the query match the target text?
 *
 * <b>WHY PLAIN `includes` IS NOT ENOUGH.</b> Brand names are inconsistent
 * about spaces: "BulkSupplements" is one word, "Transparent Labs" two. A
 * search for "bulk supplements" found nothing, because "bulksupplements"
 * doesn't contain "bulk supplements".
 *
 * Either of two rules matches:
 * 1. <b>Word by word:</b> EVERY word of the query appears in the target.
 *    "bulk supplements" -> "bulk" ✓ and "supplements" ✓. Order stops
 *    mattering too: "labs transparent" -> Transparent Labs.
 * 2. <b>Without spaces:</b> both sides compared with spaces removed.
 *    "transparentlabs" -> Transparent Labs.
 *
 * Same spirit as the backend product search: AND between words.
 */
export function matchesSearch(searchable: string, query: string): boolean {
  const target = normalizeSearchText(searchable);
  const needle = normalizeSearchText(query);
  if (!needle) return true;

  const words = needle.split(' ').filter(Boolean);
  if (words.every((word) => target.includes(word))) return true;

  return target.replace(/ /g, '').includes(needle.replace(/ /g, ''));
}
