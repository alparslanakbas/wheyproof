import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { ComparisonService } from '../core/comparison.service';

// Fixed bar that appears at the bottom once a product is added to the
// comparison. It lives in app.html, so the same bar shows whichever page the
// products were picked on (home, category, brand).
@Component({
  selector: 'app-comparison-bar',
  imports: [],
  templateUrl: './comparison-bar.html',
})
export class ComparisonBar {
  private readonly router = inject(Router);
  protected readonly comparison = inject(ComparisonService);

  protected go(): void {
    const url = this.comparison.comparisonUrl();
    if (url) this.router.navigateByUrl(url);
  }
}
