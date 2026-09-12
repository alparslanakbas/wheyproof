import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { CATEGORY_INTROS, CATEGORY_LABELS } from '../core/category-labels';
import { DealsService } from '../core/deals.service';
import { PageMetaService } from '../core/page-meta.service';
import { categoryPhosphorIcon } from '../core/nav-icons';
import { normalizeSearchText } from '../core/search-normalize';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

interface CategoryCard {
  slug: string;
  label: string;
  intro: string;
  productCount: number;
  iconClass: string;
  tone: CategoryTone;
}

type CategoryFilter = 'all' | 'performance' | 'nutrition' | 'weight';
type CategoryTone = 'violet' | 'mint' | 'blue' | 'indigo' | 'cyan' | 'rose' | 'orange';

const CATEGORY_FILTER_SLUGS: Record<Exclude<CategoryFilter, 'all'>, ReadonlySet<string>> = {
  performance: new Set(['amino-acids', 'creatine', 'pre-workout', 'hydration']),
  nutrition: new Set(['protein-powder', 'vitamins', 'protein-snacks']),
  weight: new Set(['mass-gainers', 'fat-burners']),
};

const CATEGORY_TONES: Record<string, CategoryTone> = {
  'protein-powder': 'violet',
  vitamins: 'mint',
  'amino-acids': 'blue',
  'mass-gainers': 'indigo',
  hydration: 'cyan',
  creatine: 'blue',
  'pre-workout': 'rose',
  'protein-snacks': 'orange',
  'fat-burners': 'rose',
};

// A permanent index of every category with real product counts: otherwise
// category pages were reachable only from a nav dropdown, which made them
// nearly invisible. The mobile tab bar's "Categories" target is this page.
@Component({
  selector: 'app-category-list-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './category-list-page.html',
})
export class CategoryListPage implements OnInit {
  private readonly dealsService = inject(DealsService);
  private readonly pageMeta = inject(PageMetaService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly categories = signal<CategoryCard[]>([]);
  protected readonly categorySearch = signal('');
  protected readonly activeFilter = signal<CategoryFilter>('all');
  protected readonly categoryFilters: { value: CategoryFilter; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'performance', label: 'Performance' },
    { value: 'nutrition', label: 'Nutrition' },
    { value: 'weight', label: 'Weight' },
  ];

  protected readonly filteredCategories = computed(() => {
    const query = normalizeSearchText(this.categorySearch());
    const activeFilter = this.activeFilter();

    return this.categories().filter((category) => {
      const matchesFilter =
        activeFilter === 'all' || CATEGORY_FILTER_SLUGS[activeFilter].has(category.slug);
      const searchableText = normalizeSearchText(`${category.label} ${category.intro}`);
      return matchesFilter && (!query || searchableText.includes(query));
    });
  });

  ngOnInit(): void {
    this.pageMeta.set({
      // "All Categories" matches no search intent; a title that carries the
      // topic (the H1 uses the same words).
      title: `Supplement Categories and Prices | ${SITE_NAME}`,
      description: 'Protein powder, creatine, amino acids, pre-workout and more: browse every supplement category we track, with real product counts.',
      canonicalPath: '/categories',
    });

    this.dealsService.getFilterOptions().subscribe({
      next: (options) => {
        if (options.categories.length === 0) {
          this.categories.set([]);
          this.loading.set(false);
          return;
        }

        // The real product count per category (pageSize: 1, only totalCount
        // is needed) so no invented or estimated number is shown.
        const counts$ = options.categories.map((slug) =>
          this.dealsService.getAllProducts({ categories: [slug], pageSize: 1 }).pipe(
            map((result) => result.totalCount),
            catchError(() => of(0)),
          ),
        );

        forkJoin(counts$).subscribe((counts) => {
          const cards = options.categories
            .map((slug, i) => ({
              slug,
              label: CATEGORY_LABELS[slug] ?? slug,
              intro: CATEGORY_INTROS[slug] ?? '',
              productCount: counts[i],
              iconClass: categoryPhosphorIcon(slug),
              tone: CATEGORY_TONES[slug] ?? 'violet',
            }))
            .sort((a, b) => b.productCount - a.productCount);

          this.categories.set(cards);
          this.loading.set(false);
        });
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  protected setCategorySearch(value: string): void {
    this.categorySearch.set(value);
  }

  protected setFilter(filter: CategoryFilter): void {
    this.activeFilter.set(filter);
  }
}
