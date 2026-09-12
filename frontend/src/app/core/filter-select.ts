/**
 * Behavior of the home page's brand/category/seller filter selects.
 *
 * Each select does TWO jobs: a button that ADDS a filter, and the STATUS of
 * that dimension. Selected values sit below as removable chips.
 *
 * The logic lives here because three separate bugs were exactly here:
 *
 *   1. The select read "All brands" even with a filter active: a placeholder
 *      that looked like a status.
 *   2. The selected value appeared TWICE: as the status option and as its
 *      own option in the list. The select is an "add" control, so a selected
 *      value is no longer listed (filtered in the template); chips or "All"
 *      remove it.
 *   3. Choosing "All brands" didn't clear the filter. With the select's value
 *      already empty, choosing the empty option again is not a CHANGE, so
 *      the browser never fired `change`; and even then the old handler
 *      ignored an empty value.
 */

const ACTIVE_PREFIX = '__active__';

/**
 * The value the select shows: empty (placeholder option) without a filter,
 * otherwise a status option.
 *
 * The value also carries the SELECTION COUNT. Without it, adding a second
 * brand would not change the bound value ('__active__' -> '__active__'), and
 * Angular would not write it back to the DOM; the select would freeze on the
 * last brand clicked. The same class of bug as an earlier `[value]=""`: an
 * unchanged binding is not re-applied.
 */
export function filterSelectValue(selectedCount: number): string {
  return selectedCount > 0 ? `${ACTIVE_PREFIX}:${selectedCount}` : '';
}

export type FilterSelection =
  /** A value was picked; add that filter (or remove it if already on). */
  | { readonly kind: 'toggle'; readonly value: string }
  /** "All brands/categories/sellers" was picked; clear the dimension. */
  | { readonly kind: 'clear' }
  /** The select's own status option; nothing changed. */
  | { readonly kind: 'ignore' };

export function readFilterSelection(value: string): FilterSelection {
  if (value.startsWith(ACTIVE_PREFIX)) return { kind: 'ignore' };
  return value ? { kind: 'toggle', value } : { kind: 'clear' };
}
