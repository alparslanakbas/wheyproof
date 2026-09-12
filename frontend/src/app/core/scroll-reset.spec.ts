import { NavigationSnapshot, routePath, shouldResetScroll } from './scroll-reset';

// Born from a production bug: the first click on an "On this page" link
// jumped to the top of the page, the second went to the right place. The
// path comparison included the fragment. A test would have caught it.

describe('routePath', () => {
  it('drops query parameters', () => {
    expect(routePath('/category/protein-powder?page=2')).toBe('/category/protein-powder');
  });

  it('REGRESSION: drops the fragment', () => {
    expect(routePath('/privacy#your-rights')).toBe('/privacy');
  });

  it('drops both when query and fragment come together', () => {
    expect(routePath('/brand/kaged/protein-powder?sort=price#list')).toBe('/brand/kaged/protein-powder');
  });

  it('leaves a plain path as it is', () => {
    expect(routePath('/contact')).toBe('/contact');
  });
});

describe('shouldResetScroll', () => {
  // Reference equality is enough for component identity; no real classes needed.
  const PageA = { name: 'A' };
  const PageB = { name: 'B' };
  const snapshot = (component: unknown, path: string): NavigationSnapshot => ({ component, path });

  it("doesn't reset on the first navigation", () => {
    // The document opens at the top anyway, and if the URL carries a fragment
    // (a shared section link) we mustn't undo the browser's scroll.
    expect(shouldResetScroll(null, snapshot(PageA, '/privacy'))).toBe(false);
  });

  it("REGRESSION: doesn't reset when only the fragment changed on the same page", () => {
    // routePath drops the fragment, so both paths look the same.
    const before = snapshot(PageA, '/privacy');
    const after = snapshot(PageA, routePath('/privacy#your-rights'));
    expect(shouldResetScroll(before, after)).toBe(false);
  });

  it('resets when really moving to another page', () => {
    expect(shouldResetScroll(snapshot(PageA, '/contact'), snapshot(PageB, '/guides'))).toBe(true);
  });

  it('resets for the same component on a different path', () => {
    // Links between brands stay in the same component; without comparing the
    // path, the scroll would stay where it was.
    expect(shouldResetScroll(snapshot(PageA, '/brand/kaged'), snapshot(PageA, '/brand/ghost'))).toBe(true);
  });

  it("doesn't reset when nothing changed", () => {
    expect(shouldResetScroll(snapshot(PageA, '/guides'), snapshot(PageA, '/guides'))).toBe(false);
  });

  it("doesn't reset when the product modal opens", () => {
    // Clicking a product on the home page looks like '/' -> '/product/12' but
    // it's a layer over the same page.
    expect(shouldResetScroll(snapshot(PageA, '/'), snapshot(PageA, '/product/12'))).toBe(false);
  });

  it("doesn't reset when the product modal closes", () => {
    expect(shouldResetScroll(snapshot(PageA, '/product/12'), snapshot(PageA, '/'))).toBe(false);
  });

  it('resets when going from the product modal to a real page', () => {
    // The exception only applies within the same component.
    expect(shouldResetScroll(snapshot(PageA, '/product/12'), snapshot(PageB, '/brand/kaged'))).toBe(true);
  });
});
