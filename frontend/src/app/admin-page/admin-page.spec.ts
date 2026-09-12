import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { PageMetaService } from '../core/page-meta.service';
import { AdminPage } from './admin-page';
import { AdminService } from './admin.service';

describe('AdminPage visibility safety', () => {
  const api = {
    setBrandActive: vi.fn(() => of({ id: 12, name: 'Sample brand', isActive: false })),
    setProductActive: vi.fn(() => of({ id: 34, name: 'Sample product', isActive: false })),
    brands: vi.fn(() => of([])),
    products: vi.fn(() => of([])),
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
});
