import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';

import { BODY_CALCULATORS } from '../core/body-calculators';
import { CATEGORY_LABELS } from '../core/category-labels';
import { DealsService } from '../core/deals.service';
import { FavoritesService } from '../core/favorites.service';
import {
  CALCULATOR_ICON_PATHS,
  CATEGORY_ICON_PATHS,
  DEFAULT_CATEGORY_ICON,
  calculatorIconPath,
  calculatorPhosphorIcon,
  categoryPhosphorIcon,
} from '../core/nav-icons';
import { SUPPLEMENT_DOSAGES } from '../core/supplement-dosages';
import { ThemePreference, ThemeService } from '../core/theme.service';

// Shared nav for every page except the home page (category, brand,
// watchlist, comparison, guides): logo, theme toggle and watchlist badge in
// one place. The home page keeps its own nav because it holds the search box.
@Component({
  selector: 'app-site-header',
  imports: [FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './site-header.html',
})
export class SiteHeader implements OnInit {
  private readonly dealsService = inject(DealsService);
  private readonly favoritesService = inject(FavoritesService);
  private readonly router = inject(Router);
  protected readonly theme = inject(ThemeService);

  // Search field. These pages have no product list, so the search is sent to
  // the home page, which can show the results.
  protected readonly searchQuery = signal('');

  protected submitSearch(): void {
    const term = this.searchQuery().trim();
    // An empty search does not pull the visitor away from the page they are on.
    if (!term) return;
    this.router.navigate(['/'], { queryParams: { search: term } });
  }

  // Category dropdown built from the real category list in /api/filters.
  // Without it, category pages were reachable only from the footer.
  protected readonly categories = signal<{ slug: string; label: string; iconPath: string; iconClass: string }[]>([]);
  protected readonly categoriesOpen = signal(false);

  protected readonly calculatorsOpen = signal(false);
  protected readonly calculators = [
    {
      path: '/calculators/protein',
      label: 'Daily Protein Needs',
      iconPath: CALCULATOR_ICON_PATHS.plate,
      iconClass: calculatorPhosphorIcon('protein'),
    },
    ...BODY_CALCULATORS.map((c) => ({
      path: `/calculators/${c.slug}`,
      label: c.name,
      iconPath: calculatorIconPath(c.slug),
      iconClass: calculatorPhosphorIcon(c.slug),
    })),
    ...SUPPLEMENT_DOSAGES.map((s) => ({
      path: `/calculators/${s.slug}`,
      label: `${s.name} Dosage`,
      iconPath: CALCULATOR_ICON_PATHS.capsule,
      iconClass: calculatorPhosphorIcon(s.slug),
    })),
  ];
  // The service's shared signal: adding or removing a watchlist item on any
  // page updates the badge.
  protected readonly favoritesCount = this.favoritesService.count;

  ngOnInit(): void {
    this.dealsService.getFilterOptions().subscribe((options) => {
      this.categories.set(
        options.categories.map((slug) => ({
          slug,
          label: CATEGORY_LABELS[slug] ?? slug,
          iconPath: CATEGORY_ICON_PATHS[slug] ?? DEFAULT_CATEGORY_ICON,
          iconClass: categoryPhosphorIcon(slug),
        })),
      );
    });
    this.favoritesService.ensureCount();
  }

  protected toggleCategories(): void {
    this.categoriesOpen.update((open) => !open);
  }

  protected closeCategories(): void {
    this.categoriesOpen.set(false);
  }

  protected toggleCalculators(): void {
    this.calculatorsOpen.update((open) => !open);
  }

  protected closeCalculators(): void {
    this.calculatorsOpen.set(false);
  }

  protected setTheme(preference: ThemePreference): void {
    this.theme.setPreference(preference);
  }
}
