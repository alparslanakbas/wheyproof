import { HttpErrorResponse } from '@angular/common/http';

import { describeLoadError, friendlyErrorMessage } from './friendly-error-message';

describe('friendlyErrorMessage', () => {
  it('returns a rate limit message for 429', () => {
    expect(friendlyErrorMessage(new HttpErrorResponse({ status: 429 }))).toContain('Too many requests');
  });

  it("uses the backend's own message when it sends one", () => {
    const error = new HttpErrorResponse({ status: 400, error: { message: 'Enter a valid email address.' } });
    expect(friendlyErrorMessage(error)).toBe('Enter a valid email address.');
  });

  it('falls back to the generic message for a non-HTTP error', () => {
    expect(friendlyErrorMessage(new Error('boom'), 'fallback message')).toBe('fallback message');
  });
});

// The watchlist error panel used to show three very different causes with
// one "connection problem" screen, which read as if the list were gone. Every
// variant saying the list is safe is therefore a REQUIREMENT, not decoration.
describe('describeLoadError', () => {
  it('describes 429 as temporary and gives the code', () => {
    const info = describeLoadError(new HttpErrorResponse({ status: 429 }));
    expect(info.code).toBe('HTTP 429');
    expect(info.title).not.toBe('');
    expect(info.message).toContain('safe');
  });

  it('says a server error is on our side', () => {
    const info = describeLoadError(new HttpErrorResponse({ status: 504 }));
    expect(info.code).toBe('HTTP 504');
    expect(info.message).toContain('on our side');
  });

  it('puts 500 and 503 in the server error branch too', () => {
    expect(describeLoadError(new HttpErrorResponse({ status: 500 })).code).toBe('HTTP 500');
    expect(describeLoadError(new HttpErrorResponse({ status: 503 })).code).toBe('HTTP 503');
  });

  it('shows no code for a network error (status 0)', () => {
    // Angular reports status 0 when the request never reached the server;
    // there's no meaningful code to show.
    const info = describeLoadError(new HttpErrorResponse({ status: 0 }));
    expect(info.code).toBeNull();
    expect(info.title.toLowerCase()).toContain('connect');
  });

  it('shows no code for a non-HTTP error either', () => {
    expect(describeLoadError(new Error('unexpected')).code).toBeNull();
  });

  it('still passes the code on in other cases', () => {
    expect(describeLoadError(new HttpErrorResponse({ status: 418 })).code).toBe('HTTP 418');
  });

  it('EVERY variant says the list is still there', () => {
    // The real requirement: no variant may lead someone to conclude "my list
    // was deleted".
    const variants = [
      describeLoadError(new HttpErrorResponse({ status: 429 })),
      describeLoadError(new HttpErrorResponse({ status: 504 })),
      describeLoadError(new HttpErrorResponse({ status: 0 })),
      describeLoadError(new HttpErrorResponse({ status: 418 })),
    ];
    for (const v of variants) {
      const text = `${v.title} ${v.message}`.toLowerCase();
      expect(text).toContain('list');
      expect(text).not.toMatch(/deleted|gone|lost forever/);
    }
  });
});
