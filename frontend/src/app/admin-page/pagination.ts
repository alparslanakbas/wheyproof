/**
 * Input of the template's shared pagination block (`#pagination`). The product, brand
 * and subscriber lists use the same block (2026-10-04; there used to be three copies
 * and the template was at its size ceiling).
 */
export interface Pagination {
  /** A range text such as "1–50 of 230 rows". */
  summary: string;
  page: number;
  pageCount: number;
  pages: number[];
  /** The buttons lock while a request runs. */
  busy: boolean;
  goTo: (page: number) => void;
}

/** At most five visible page numbers, the current one in the middle where possible. */
export function visiblePages(page: number, pageCount: number): number[] {
  const start = Math.min(Math.max(1, page - 2), Math.max(1, pageCount - 4));
  return Array.from({ length: Math.min(5, pageCount) }, (_, index) => start + index);
}
