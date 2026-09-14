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
    products: vi.fn(() => of([])),
    subscribers: vi.fn(() => of({ subscribers: [], summary: { total: 0, active: 0, pending: 0, unsubscribed: 0 } })),
    deactivateSubscriber: vi.fn(() => of({})),
    sendSubscriberConfirmation: vi.fn(() => of({ message: 'sent' })),
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

  it('shows the backend reason when a confirmation email is refused', () => {
    api.sendSubscriberConfirmation.mockReturnValueOnce(
      throwError(() => ({ status: 429, error: { message: 'A confirmation email went out less than 5 minutes ago.' } })),
    );

    page.sendConfirmation({ ...subscriber, status: 'pending', confirmedAt: null });

    expect(page.subscriberMessage()).toBe('A confirmation email went out less than 5 minutes ago.');
    expect(page.subscriberBusyId()).toBeNull();
  });
});
