import { clampTitle } from './meta-description';

/**
 * Page numbers to show in the pagination bar.
 *
 * <b>WHY.</b> List pagination used to be `<button (click)>` only, so the
 * server HTML carried no pagination LINKS and Google saw the first 24
 * products of each list. On the Turkish site nine category pages linked to
 * 216 unique products out of 4,713; the rest sat only in the sitemap as
 * "Discovered - currently not indexed". A sitemap gives discovery, not
 * priority; internal links give priority.
 *
 * <b>WHY NOT JUST PREV/NEXT.</b> Thousands of products at 24 per page means
 * hundreds of pages; with only "next" the last page would sit hundreds of
 * clicks deep and no crawler walks that chain to the end. A numbered window
 * plus first/last links cuts the depth to roughly a quarter.
 *
 * `null` stands for the "…" gap.
 */
export type PageItem = number | null;

export function pageWindow(current: number, total: number, radius = 2): PageItem[] {
  if (!Number.isFinite(total) || total < 1) return [];

  const validTotal = Math.floor(total);
  const validCurrent = Math.min(Math.max(Math.floor(current) || 1, 1), validTotal);

  // First and last page ALWAYS appear: "back to start/end" for people, two
  // fixed endpoints that shorten the depth for crawlers.
  const numbers = new Set<number>([1, validTotal]);
  for (let i = validCurrent - radius; i <= validCurrent + radius; i++) {
    if (i >= 1 && i <= validTotal) numbers.add(i);
  }

  const sorted = [...numbers].sort((a, b) => a - b);
  const result: PageItem[] = [];
  let previous = 0;
  for (const number of sorted) {
    const gap = number - previous;
    if (previous > 0 && gap === 2) {
      // Hiding a SINGLE page behind "…" saves no space and wastes a free
      // internal link.
      result.push(number - 1);
    } else if (previous > 0 && gap > 2) {
      result.push(null);
    }
    result.push(number);
    previous = number;
  }
  return result;
}

/**
 * Adds the page number to a title.
 *
 * <b>WHY THE EFFORT.</b> The page number is the ONLY thing that tells the
 * paginated addresses apart; lose it and they all share one title and Google
 * treats them as duplicates, which crawlable pagination was meant to avoid.
 *
 * `clampTitle` first drops everything after the last " | ", then trims at a
 * word boundary. Neither appending the suffix nor placing it before the
 * separator is enough: the first attempt put it before the separator and a
 * real 87-character brand x category title still cut it (a test caught it).
 * So the suffix is not appended, it gets RESERVED room: the brand tail goes
 * first, then the end of the subject.
 *
 * The `max` default must match `clampTitle`.
 */
export function paginatedTitle(title: string, page: number, max = 65): string {
  if (page <= 1) return title;

  const suffix = ` – Page ${page}`;
  const separator = title.lastIndexOf(' | ');
  const subject = separator < 0 ? title : title.slice(0, separator);
  const tail = separator < 0 ? '' : title.slice(separator);

  // 1st choice: everything fits.
  if (subject.length + suffix.length + tail.length <= max) return `${subject}${suffix}${tail}`;

  // 2nd choice: drop the brand tail. It is the same on every title and tells
  // nothing apart; the page number does.
  if (subject.length + suffix.length <= max) return `${subject}${suffix}`;

  // 3rd choice: trim the subject. clampTitle sees no separator here and trims
  // at a word boundary.
  return `${clampTitle(subject, max - suffix.length)}${suffix}`;
}

/**
 * Reads the `page` query parameter. A broken or missing value falls back to
 * 1: people can type "?page=abc" and the list must not come up empty.
 */
export function pageFromQuery(raw: string | null): number {
  const value = Number(raw);
  return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 1;
}
