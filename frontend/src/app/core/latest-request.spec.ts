import { Subject } from 'rxjs';
import { LatestRequest } from './latest-request';

// The race on the list pages: with quick filter changes a slow OLD response
// could overwrite the new one.

describe('LatestRequest', () => {
  it('a stale response arriving late does not overwrite the new one', () => {
    const latest = new LatestRequest();
    const slow = new Subject<string>();
    const fast = new Subject<string>();
    let shown = '';

    latest.run(slow, { next: (v) => (shown = v) });
    latest.run(fast, { next: (v) => (shown = v) });

    fast.next('new filter');
    slow.next('old filter'); // out of order, arrives late

    expect(shown).toBe('new filter');
  });

  it('the previous request is unsubscribed (the HTTP request is cancelled)', () => {
    const latest = new LatestRequest();
    const first = new Subject<string>();

    latest.run(first, {});
    expect(first.observed).toBe(true);

    latest.run(new Subject<string>(), {});
    expect(first.observed).toBe(false);
  });

  it('errors still reach the observer', () => {
    const latest = new LatestRequest();
    const request = new Subject<string>();
    let failed = false;

    latest.run(request, { error: () => (failed = true) });
    request.error(new Error('network'));

    expect(failed).toBe(true);
  });
});
