import { describe, expect, it } from 'vitest';

import { API_BASE_URL } from './api.config';
import { toInternalApiUrl } from './internal-api';

describe('toInternalApiUrl', () => {
  it('leaves the URL alone without an internal base (local dev, browser)', () => {
    expect(toInternalApiUrl(`${API_BASE_URL}/api/deals`, null)).toBe(`${API_BASE_URL}/api/deals`);
  });

  it('swaps the API prefix and keeps path and query', () => {
    expect(toInternalApiUrl(`${API_BASE_URL}/api/deals?page=2`, 'http://wheyproof-backend:8080/')).toBe(
      'http://wheyproof-backend:8080/api/deals?page=2',
    );
  });

  it('ignores other hosts, look-alike prefixes and relative URLs', () => {
    const base = 'http://wheyproof-backend:8080';
    expect(toInternalApiUrl('https://example.com/api/deals', base)).toBe('https://example.com/api/deals');
    expect(toInternalApiUrl(`${API_BASE_URL}.evil.test/x`, base)).toBe(`${API_BASE_URL}.evil.test/x`);
    expect(toInternalApiUrl('/admin/api/status', base)).toBe('/admin/api/status');
  });
});
