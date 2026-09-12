import { filterSelectValue, readFilterSelection } from './filter-select';

describe('filterSelectValue', () => {
  it('shows the placeholder option when no filter is set', () => {
    expect(filterSelectValue(0)).toBe('');
  });

  it('REGRESSION: the VALUE changes when the selection count changes', () => {
    // With a constant value, adding a second brand wouldn't make Angular
    // write the binding back to the DOM, and the box would freeze on the last
    // clicked brand's name. Seen exactly so in the browser.
    expect(filterSelectValue(1)).not.toBe(filterSelectValue(2));
    expect(filterSelectValue(2)).not.toBe(filterSelectValue(3));
  });

  it('NEVER collides with the placeholder while a filter is set', () => {
    // If they were equal, picking "All brands" wouldn't count as a change and
    // the browser would never fire the change event: the reported bug.
    for (const n of [1, 2, 10]) expect(filterSelectValue(n)).not.toBe('');
  });
});

describe('readFilterSelection', () => {
  it('toggles the filter when a real value is picked', () => {
    expect(readFilterSelection('Kaged')).toEqual({ kind: 'toggle', value: 'Kaged' });
  });

  it('REGRESSION: an empty value means "all", not "ignore"', () => {
    // The old handler said `if (value)`; "All brands" did nothing.
    expect(readFilterSelection('')).toEqual({ kind: 'clear' });
  });

  it("doesn't apply the box's own status option as a filter", () => {
    // Otherwise "3 brands selected" would be added as a brand filter.
    expect(readFilterSelection(filterSelectValue(3))).toEqual({ kind: 'ignore' });
  });
});
