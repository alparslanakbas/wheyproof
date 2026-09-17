import { Pipe, PipeTransform } from '@angular/core';

/** Presentation only: retain the API's wording without displaying JSON escaping. */
@Pipe({ name: 'adminFailureReason' })
export class AdminFailureReason implements PipeTransform {
  transform(reason: string | null): string {
    if (!reason) return '—';
    try {
      const parsed: unknown = JSON.parse(reason);
      if (
        parsed &&
        typeof parsed === 'object' &&
        'message' in parsed &&
        typeof parsed.message === 'string'
      ) {
        return parsed.message;
      }
    } catch {
      // Plain-text responses are already suitable for display.
    }
    return reason;
  }
}
