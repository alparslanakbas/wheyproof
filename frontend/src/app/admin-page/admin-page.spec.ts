import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { PageMetaService } from '../core/page-meta.service';
import { AdminPage } from './admin-page';
import { AdminService } from './admin.service';

describe('AdminPage visibility safety', () => {
  const api = {
    setBrandActive: vi.fn(() => of({ id: 12, name: 'Sample brand', isActive: false })),
    setProductActive: vi.fn(() => of({ id: 34, name: 'Sample product', isActive: false })),
    brands: vi.fn(() => of([])),
    products: vi.fn((..._args: unknown[]) => of({ items: [] as unknown[], total: 0, page: 1, pageSize: 50 })),
    subscribers: vi.fn(() => of({ subscribers: [], summary: { total: 0, active: 0, pending: 0, unsubscribed: 0 } })),
    deactivateSubscriber: vi.fn(() => of({})),
    sendSubscriberConfirmation: vi.fn(() => of({ message: 'sent' })),
    setProductNutrition: vi.fn(() => of({ rowsUpdated: 3 })),
    setProductCategory: vi.fn(() => of({ rowsUpdated: 3 })),
    clearProductNutrition: vi.fn(() => of({ rowsUpdated: 3 })),
  };

  const product = {
    id: 21,
    name: 'Double Chocolate Whey Protein Powder 2LB',
    brand: 'Naked Nutrition',
    seller: null,
    isActive: true,
    latestPrice: 49.99,
    category: 'protein-powder',
    categoryIsManual: false,
    nutritionJson: '{"Serving Size":"43g","Calories":"160","Total Fat":"2.5g","Total Carbohydrate":"11g","Protein":"25g"}',
    nutritionIsManual: false,
    servingSizeGrams: 43,
  };

  const subscriber = {
    id: 7,
    email: 'reader@example.com',
    status: 'active' as const,
    subscribedAt: '2026-09-13T02:40:51Z',
    confirmedAt: '2026-09-13T16:45:22Z',
    unsubscribedAt: null,
    lastConfirmationEmailSentAt: null,
    lastDigestSentAt: null,
    watchCount: 0,
    favoriteCount: 0,
  };

  let page: AdminPage;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminService, useValue: api },
        { provide: PageMetaService, useValue: { set: vi.fn() } },
        { provide: PLATFORM_ID, useValue: 'server' },
      ],
    });
    page = TestBed.runInInjectionContext(() => new AdminPage());
  });

  it('asks for confirmation before hiding, and updates only after it', () => {
    page.requestVisibilityChange('brand', 12, 'Sample brand', true);

    expect(page.pendingChange()).toEqual({ type: 'brand', id: 12, name: 'Sample brand' });
    expect(api.setBrandActive).not.toHaveBeenCalled();

    page.confirmChange();

    expect(api.setBrandActive).toHaveBeenCalledWith(12, false);
    expect(page.pendingChange()).toBeNull();
    expect(page.visibilityMessage()).toBe('Sample brand hidden.');
  });

  it('publishes a hidden item again without asking', () => {
    page.requestVisibilityChange('product', 34, 'Sample product', false);

    expect(page.pendingChange()).toBeNull();
    expect(api.setProductActive).toHaveBeenCalledWith(34, true);
  });

  it('asks before deactivating a subscriber, and deactivates only after it', () => {
    page.requestDeactivation(subscriber);

    expect(page.pendingDeactivation()).toEqual(subscriber);
    expect(api.deactivateSubscriber).not.toHaveBeenCalled();

    page.confirmDeactivation();

    expect(api.deactivateSubscriber).toHaveBeenCalledWith(7);
    expect(page.pendingDeactivation()).toBeNull();
    expect(api.subscribers).toHaveBeenCalled();
  });

  it('prefills the editor from the stored table and sends numbers, blank as null', () => {
    page.openDataEditor(product);
    expect(page.editingData()?.protein).toBe('25');
    expect(page.editingData()?.fat).toBe('2.5');

    page.saveNutrition();

    expect(api.setProductNutrition).toHaveBeenCalledWith(21, {
      servingSizeGrams: 43,
      calories: 160,
      proteinGrams: 25,
      carbohydrateGrams: 11,
      fatGrams: 2.5,
      fiberGrams: null,
      otherRows: [],
    });
  });

  // A creatine label: no macros, one named row. The stored table's other rows
  // come back into the editor; a shape it can't edit is named, not dropped silently.
  it('reads stored supplement rows back, names rows it cannot edit, and sends typed rows', () => {
    page.openDataEditor({
      ...product,
      category: 'creatine',
      servingSizeGrams: 5,
      nutritionJson: '{"Serving Size":"5g","Creatine Monohydrate":"5g","Vitamin D3":"25mcg","Iron":"10%"}',
    });

    const form = page.editingData()!;
    expect(form.otherRows).toEqual([
      { label: 'Creatine Monohydrate', amount: '5', unit: 'g' },
      { label: 'Vitamin D3', amount: '25', unit: 'mcg' },
    ]);
    expect(page.dataMessage()).toContain('Iron');

    page.removeOtherRow(1);
    page.addOtherRow();
    page.updateOtherRow(1, 'label', 'Caffeine');
    page.updateOtherRow(1, 'amount', 200);
    page.saveNutrition();

    expect(api.setProductNutrition).toHaveBeenCalledWith(21, {
      servingSizeGrams: 5,
      calories: null,
      proteinGrams: null,
      carbohydrateGrams: null,
      fatGrams: null,
      fiberGrams: null,
      otherRows: [
        { label: 'Creatine Monohydrate', amount: 5, unit: 'g' },
        { label: 'Caffeine', amount: 200, unit: 'mg' },
      ],
    });
  });

  it('adds the category template once and skips its rows left without an amount', () => {
    page.openDataEditor({ ...product, category: 'pre-workout', nutritionJson: null });
    expect(page.templateCategoryLabel(page.editingData()!)).toBe('Pre-Workout');

    page.addTemplateRows();
    const labels = page.editingData()!.otherRows.map((r) => r.label);
    expect(labels).toEqual(['Caffeine', 'L-Citrulline', 'Beta-Alanine', 'Betaine Anhydrous']);
    // Everything is in: the button goes away instead of adding duplicates.
    expect(page.templateCategoryLabel(page.editingData()!)).toBeNull();

    page.updateOtherRow(0, 'amount', '300');
    page.saveNutrition();

    expect(api.setProductNutrition).toHaveBeenCalledWith(
      21,
      expect.objectContaining({ otherRows: [{ label: 'Caffeine', amount: 300, unit: 'mg' }] }),
    );
  });

  it('uses the category being set in the editor for the template', () => {
    page.openDataEditor({ ...product, category: null, nutritionJson: null });
    expect(page.templateCategoryLabel(page.editingData()!)).toBeNull();

    page.updateDataField('category', 'amino-acids');

    expect(page.templateCategoryLabel(page.editingData()!)).toBe('Amino Acids');
  });

  it('shows the backend reason when typed nutrition is refused', () => {
    api.setProductNutrition.mockReturnValueOnce(
      throwError(() => ({ status: 400, error: { message: "calories 400 don't match the macros (expected 160-166)" } })),
    );
    page.openDataEditor(product);
    page.updateDataField('calories', 400);

    page.saveNutrition();

    expect(page.dataMessage()).toBe("calories 400 don't match the macros (expected 160-166)");
    expect(page.dataSaving()).toBe(false);
  });

  // The "missing nutrition" worklist matched thousands of rows and the list
  // used to stop at 200 with no way to reach the rest.
  it('pages the product list and keeps the page after an edit', () => {
    api.products.mockImplementation((...args: unknown[]) =>
      of({ items: [product] as unknown[], total: 1234, page: (args[4] as number) ?? 1, pageSize: 50 }),
    );

    page.onDataFilterChange('missingNutrition', true);
    expect(api.products).toHaveBeenLastCalledWith('', false, true, false, 1);
    expect(page.productPageCount()).toBe(25);

    page.goToProductPage(3);
    expect(api.products).toHaveBeenLastCalledWith('', false, true, false, 3);
    expect(page.productPage()).toBe(3);
    expect(page.productRangeStart()).toBe(101);

    // Past the last page is clamped, not requested.
    page.goToProductPage(99);
    expect(api.products).toHaveBeenLastCalledWith('', false, true, false, 25);

    page.goToProductPage(3);
    page.openDataEditor(product);
    page.saveCategory();
    expect(api.products).toHaveBeenLastCalledWith('', false, true, false, 3);

    // A new filter starts over at page 1.
    page.onDataFilterChange('uncategorised', true);
    expect(api.products).toHaveBeenLastCalledWith('', false, true, true, 1);
  });

  // Saving rows out of the filtered list can leave the viewed page past the end.
  it('falls back to the last page when the current one emptied', () => {
    api.products.mockImplementation((...args: unknown[]) => {
      const requested = (args[4] as number) ?? 1;
      return of({ items: (requested > 2 ? [] : [product]) as unknown[], total: 60, page: requested, pageSize: 50 });
    });

    page.onDataFilterChange('missingNutrition', true);
    page.searchProducts(3);

    expect(api.products).toHaveBeenLastCalledWith('', false, true, false, 2);
    expect(page.productPage()).toBe(2);
  });

  it('sends null to put a product back on the automatic category', () => {
    page.openDataEditor({ ...product, categoryIsManual: true, category: 'vitamins' });
    page.updateDataField('category', '');

    page.saveCategory();

    expect(api.setProductCategory).toHaveBeenCalledWith(21, null);
  });

  it('shows the backend reason when a confirmation email is refused', () => {
    api.sendSubscriberConfirmation.mockReturnValueOnce(
      throwError(() => ({ status: 429, error: { message: 'A confirmation email went out less than 5 minutes ago.' } })),
    );

    page.sendConfirmation({ ...subscriber, status: 'pending', confirmedAt: null });

    expect(page.subscriberMessage()).toBe('A confirmation email went out less than 5 minutes ago.');
    expect(page.subscriberBusyId()).toBeNull();
  });
});
