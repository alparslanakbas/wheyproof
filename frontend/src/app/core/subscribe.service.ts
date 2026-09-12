import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from './api.config';

@Injectable({ providedIn: 'root' })
export class SubscribeService {
  private readonly http = inject(HttpClient);

  /**
   * `website` is a honeypot: hidden in the form, never seen by a real visitor
   * and sent empty. Bots that auto-fill forms fill it; the server silently
   * ignores such requests.
   */
  subscribe(email: string, website = ''): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API_BASE_URL}/api/subscribe`, { email, website });
  }
}
