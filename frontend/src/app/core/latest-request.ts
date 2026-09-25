import { Observable, Observer, Subscription } from 'rxjs';

/**
 * Accepts only the LATEST request's response: when a new request starts the
 * previous one is cancelled (the HTTP request too), so its response is never
 * handled.
 *
 * WHY (security/architecture review, 2026-09-26): the list pages started a new
 * request on every filter change and left the old one running. With quick
 * filter changes a slow OLD response could overwrite the new one: the selected
 * filter says one thing, the listed products another. It's what `switchMap`
 * does; the pages start requests by method call rather than from a stream, so
 * it's a small class.
 */
export class LatestRequest {
  private active?: Subscription;

  run<T>(request$: Observable<T>, observer: Partial<Observer<T>>): void {
    this.active?.unsubscribe();
    this.active = request$.subscribe(observer);
  }
}
