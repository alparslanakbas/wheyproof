import { computed, signal } from '@angular/core';

import { AdminService, AdminSubscriber, SubscriberFilter, SubscribersResponse } from './admin.service';
import { Pagination, visiblePages } from './pagination';

/** A subscriber action waiting for confirmation: deactivate or delete permanently. */
export interface PendingSubscriberAction {
  subscriber: AdminSubscriber;
  kind: 'deactivate' | 'delete';
}

/**
 * The admin panel's Subscribers tab. Split out of the page component on 2026-10-04: the
 * panel files were at their size ceiling and paging plus permanent delete didn't fit.
 *
 * The list is paged and filtered ON THE SERVER (the product list's pattern). It used to
 * load the newest 1000 rows at once and search in the browser; a subscriber past 1000
 * never showed up.
 */
export class SubscriberAdmin {
  readonly data = signal<SubscribersResponse | null>(null);
  readonly loading = signal(false);
  readonly search = signal('');
  readonly filter = signal<SubscriberFilter>('all');
  readonly message = signal<string | null>(null);
  readonly pendingAction = signal<PendingSubscriberAction | null>(null);
  /** Id of the row whose request is running, so only that row's buttons disable. */
  readonly busyId = signal<number | null>(null);
  readonly page = signal(1);

  readonly pageCount = computed(() => {
    const data = this.data();
    return data ? Math.max(1, Math.ceil(data.total / Math.max(1, data.pageSize))) : 1;
  });

  readonly pagination = computed<Pagination>(() => {
    const data = this.data();
    const page = this.page();
    const first = data && data.subscribers.length ? (page - 1) * data.pageSize + 1 : 0;
    const last = first ? first + (data?.subscribers.length ?? 1) - 1 : 0;
    return {
      summary: `${first}–${last} of ${data?.total ?? 0} subscribers`,
      page,
      pageCount: this.pageCount(),
      pages: visiblePages(page, this.pageCount()),
      busy: this.loading(),
      goTo: (target) => this.load(target),
    };
  });

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(
    private readonly api: AdminService,
    private readonly errorText: (e: unknown, fallback: string) => string,
  ) {}

  /** Loaded on every visit, not once: a subscription can be confirmed from an inbox meanwhile. */
  load(page = this.page()): void {
    this.loading.set(true);
    this.api.subscribers({ search: this.search(), status: this.filter(), page }).subscribe({
      next: (data) => {
        this.data.set(data);
        this.page.set(data.page);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.message.set(this.errorText(e, "Couldn't load subscribers."));
      },
    });
  }

  /** Don't send a request per keystroke; the first page after a short pause. */
  searchChanged(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(1), 300);
  }

  selectFilter(filter: SubscriberFilter): void {
    this.filter.set(filter);
    this.load(1);
  }

  /** Deactivating and deleting both stop someone's email; it asks first. */
  requestAction(subscriber: AdminSubscriber, kind: PendingSubscriberAction['kind']): void {
    this.message.set(null);
    this.pendingAction.set({ subscriber, kind });
  }

  cancelAction(): void {
    if (this.busyId() !== null) return;
    this.pendingAction.set(null);
  }

  confirmAction(): void {
    const action = this.pendingAction();
    if (!action) return;
    const { subscriber, kind } = action;
    const deleting = kind === 'delete';

    this.busyId.set(subscriber.id);
    (deleting
      ? this.api.deleteSubscriber(subscriber.id)
      : this.api.deactivateSubscriber(subscriber.id)
    ).subscribe({
      next: () => {
        this.busyId.set(null);
        this.pendingAction.set(null);
        this.message.set(
          deleting
            ? `${subscriber.email} was deleted permanently.`
            : `${subscriber.email} is no longer subscribed.`,
        );
        // If the page's only row was deleted the page is empty now: go back one.
        const leftOnPage = (this.data()?.subscribers.length ?? 0) - (deleting ? 1 : 0);
        this.load(leftOnPage > 0 ? this.page() : Math.max(1, this.page() - 1));
      },
      error: (e) => {
        this.busyId.set(null);
        this.pendingAction.set(null);
        this.message.set(
          this.errorText(
            e,
            deleting ? "Couldn't delete the subscriber." : "Couldn't deactivate the subscriber.",
          ),
        );
      },
    });
  }

  sendConfirmation(subscriber: AdminSubscriber): void {
    this.message.set(null);
    this.busyId.set(subscriber.id);
    this.api.sendSubscriberConfirmation(subscriber.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.message.set(`Confirmation email sent to ${subscriber.email}.`);
        this.load();
      },
      error: (e) => {
        this.busyId.set(null);
        // The backend explains the cooldown and provider failures in its own
        // words; a generic "failed" would hide which one happened.
        const body = (e as { error?: unknown } | null)?.error;
        const message =
          typeof body === 'string' ? body : (body as { message?: string } | null)?.message;
        this.message.set(
          message?.trim() || this.errorText(e, "Couldn't send the confirmation email."),
        );
      },
    });
  }

  statusLabel(status: SubscriberFilter): string {
    switch (status) {
      case 'all':
        return 'All';
      case 'active':
        return 'Active';
      case 'pending':
        return 'Awaiting confirmation';
      default:
        return 'Unsubscribed';
    }
  }
}
