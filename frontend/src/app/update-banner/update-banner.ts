import { Component, inject, signal } from '@angular/core';

import { AppUpdateService } from '../core/app-update.service';

@Component({
  selector: 'app-update-banner',
  templateUrl: './update-banner.html',
})
export class UpdateBanner {
  protected readonly updates = inject(AppUpdateService);
  // Not persisted (no localStorage): even when dismissed this is a real new
  // version, and the next visit or reload gets it anyway. A component-level
  // signal is enough to act as "remind me later" for this session.
  protected readonly dismissed = signal(false);
}
