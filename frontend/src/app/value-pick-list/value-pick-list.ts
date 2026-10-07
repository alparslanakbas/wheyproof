import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { DealsService } from '../core/deals.service';
import { displayName } from '../core/display-name';
import { LatestRequest } from '../core/latest-request';
import { PricePipe } from '../core/price.pipe';
import { PriceHistoryService } from '../core/price-history.service';
import { productPath } from '../core/product-link';
import { GoalPickSection } from '../core/supplement-goals';
import { PRICE_UNIT, unitPrice } from '../core/unit-price';
import { ValuePick } from '../core/value-pick.model';

// The list stays short: the page's job is to help decide, not to browse the
// catalog. Every section links to its category page for the rest.
const PICK_COUNT = 6;

const PROTEIN_TYPES: { value: string | null; label: string }[] = [
  { value: null, label: 'All' },
  { value: 'isolate', label: 'Low lactose (isolate)' },
  { value: 'plant', label: 'Plant-based' },
];

// A product list on a "Which supplement?" goal page: lowest price per pound
// (US) or kilogram (UK) in a category, one product per brand (see
// /api/value-picks).
@Component({
  selector: 'app-value-pick-list',
  imports: [PricePipe, RouterLink],
  templateUrl: './value-pick-list.html',
})
export class ValuePickList implements OnInit {
  private readonly dealsService = inject(DealsService);
  private readonly priceHistoryService = inject(PriceHistoryService);
  private readonly request = new LatestRequest();

  readonly section = input.required<GoalPickSection>();
  /** The starting type from the quiz's dairy answer (protein list only). */
  readonly initialType = input<string | null>(null);
  /** How many products to show; the endpoint returns at most 12 (ValuePickRanker.MaxCount). */
  readonly count = input(PICK_COUNT);
  /** On goal pages the list sits under an h2 (h3); on the list's own page the title is the h2. */
  readonly headingLevel = input<'h2' | 'h3'>('h3');

  protected readonly displayName = displayName;
  protected readonly productPath = productPath;
  protected readonly proteinTypes = PROTEIN_TYPES;
  protected readonly unitLabel = PRICE_UNIT.label;
  protected readonly unitPrice = unitPrice;

  protected readonly type = signal<string | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly picks = signal<ValuePick[]>([]);
  protected readonly eligibleCount = signal(0);
  protected readonly skeletonRows = Array.from({ length: 4 }, (_, i) => i);

  ngOnInit(): void {
    const section = this.section();
    this.type.set(section.proteinFilter ? this.initialType() : (section.type ?? null));
    this.load();
  }

  protected setProteinType(value: string | null): void {
    if (this.type() === value) return;
    this.type.set(value);
    this.load();
  }

  protected trackStoreClick(pick: ValuePick): void {
    this.priceHistoryService.trackStoreClick(pick.productId);
  }

  private load(): void {
    this.state.set('loading');
    this.request.run(this.dealsService.getValuePicks(this.section().category, this.type(), this.count()), {
      next: (result) => {
        this.picks.set(result.items);
        this.eligibleCount.set(result.eligibleCount);
        this.state.set('ready');
      },
      error: () => this.state.set('error'),
    });
  }
}
