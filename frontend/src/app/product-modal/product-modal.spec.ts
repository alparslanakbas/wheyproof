import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { ProductModal } from './product-modal';

// This component's chart helpers caused real production bugs three times, so
// the regression tests focus here. Hover/dedupe/tooltip math is shared with
// the review page and lives in core/chart-hover.ts, with its tests. The
// methods below take `points` and compute purely, so they're called without
// setting the `deal` input (and without detectChanges, which would fire the
// constructor's effects); no HTTP or route mocks needed.
describe('ProductModal: price chart helpers', () => {
  let component: any;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProductModal],
      providers: [
        provideHttpClient(),
        // The template's RouterLinks need ActivatedRoute while the view is
        // created, even without detectChanges().
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})) } },
      ],
    });
    component = TestBed.createComponent(ProductModal).componentInstance;
  });

  describe('buildXAxisLabels', () => {
    it('returns an empty array for no data', () => {
      expect(component.buildXAxisLabels([])).toEqual([]);
    });

    it('removes consecutive duplicate labels over a short range', () => {
      const points = [
        { price: 100, scrapedAt: '2026-08-11T12:00:00Z' },
        { price: 100, scrapedAt: '2026-08-11T20:00:00Z' },
      ];

      const labels = component.buildXAxisLabels(points);
      const texts = labels.map((l: { label: string }) => l.label);
      const hasConsecutiveDuplicate = texts.some((t: string, i: number) => i > 0 && t === texts[i - 1]);

      expect(hasConsecutiveDuplicate).toBe(false);
    });

    it('returns one centered label for a single point in time', () => {
      const points = [{ price: 100, scrapedAt: '2026-08-11T12:00:00Z' }];

      const labels = component.buildXAxisLabels(points);

      expect(labels.length).toBe(1);
      expect(labels[0].x).toBe(300); // CHART_WIDTH / 2
    });
  });

  describe('discountEventCount', () => {
    it('counts consecutive price drops, not increases', () => {
      component.points.set([
        { price: 50, scrapedAt: '2026-08-01T00:00:00Z' },
        { price: 40, scrapedAt: '2026-08-05T00:00:00Z' }, // drop
        { price: 45, scrapedAt: '2026-08-08T00:00:00Z' }, // increase
        { price: 35, scrapedAt: '2026-08-12T00:00:00Z' }, // drop
      ]);

      expect(component.discountEventCount()).toBe(2);
    });

    it('returns 0 when there are no drops', () => {
      component.points.set([
        { price: 10, scrapedAt: '2026-08-01T00:00:00Z' },
        { price: 20, scrapedAt: '2026-08-05T00:00:00Z' },
      ]);

      expect(component.discountEventCount()).toBe(0);
    });
  });
});
