import { HttpErrorResponse } from '@angular/common/http';

// Email-sensitive endpoints (watchlist add, "notify me", recovery link,
// newsletter) share one IP-based rate limit on the backend ("EmailSensitive",
// 5 requests per 5 minutes). A 429 is told apart instead of disappearing
// behind a generic "something went wrong".
const RATE_LIMIT_MESSAGE = 'Too many requests in a short time. Please try again in a few minutes.';

export function friendlyErrorMessage(error: unknown, genericMessage = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) return genericMessage;
  if (error.status === 429) return RATE_LIMIT_MESSAGE;

  // When the backend returns a readable { message: "..." } body (a 400
  // validation error, a 502 "email can't be sent right now"), it is always
  // more useful than the generic text, and a new backend error needs no
  // frontend change.
  const backendMessage = (error.error as { message?: unknown } | null)?.message;
  return typeof backendMessage === 'string' && backendMessage.trim().length > 0 ? backendMessage : genericMessage;
}

// Three very different causes used to share one "connection problem"
// message: rate limiting (429), a server error (5xx) and a request that never
// reached the server. They are not the same thing to a visitor, and the old
// text read as if the list were GONE. Every variant says the list is safe on
// the server.
//
// The code (HTTP 429/504) is shown in small print: it should not alarm
// anyone, but when a problem is reported we don't have to guess which it was.
export interface LoadErrorInfo {
  label: string;
  title: string;
  message: string;
  code: string | null;
}

export function describeLoadError(error: unknown): LoadErrorInfo {
  const status = error instanceof HttpErrorResponse ? error.status : null;

  if (status === 429) {
    return {
      label: 'Busy',
      title: 'That was a bit fast',
      message: 'Too many requests in a short time. Your watchlist is safe; we will retry automatically in a few seconds.',
      code: 'HTTP 429',
    };
  }

  // Angular reports network-level failures (offline, DNS, a blocking
  // extension) as status 0: the server never answered.
  if (status === null || status === 0) {
    return {
      label: 'Connection',
      title: "Couldn't connect to the internet",
      message: 'Your watchlist is safe on our server. Check your connection and try again.',
      code: null,
    };
  }

  if (status >= 500) {
    return {
      label: 'Temporary problem',
      title: 'Our server is having a temporary problem',
      message: 'This is on our side and nothing on your watchlist was lost. Please try again in a moment.',
      code: `HTTP ${status}`,
    };
  }

  return {
    label: 'Unexpected',
    title: "Your watchlist couldn't be opened right now",
    message: 'Your list is still on our server. It will most likely open when you try again.',
    code: `HTTP ${status}`,
  };
}
