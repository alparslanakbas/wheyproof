import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ThemeService } from '../core/theme.service';
import { BrandPage } from './brand-page';

// comparisonPairSlug builds the canonical URL of brand comparison pages. The
// same pair must map to the same URL whatever the order (by design, to avoid
// duplicate content); that's the one real behavioral guarantee here.
describe('BrandPage: comparisonPairSlug', () => {
  let component: any;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BrandPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})) } },
        // BrandPage itself doesn't depend on ThemeService, but its template's
        // <app-site-header> does. TestBed.createComponent instantiates child
        // components while creating the view even without detectChanges(), so
        // the real ThemeService constructor (which calls window.matchMedia,
        // missing in jsdom) would run. A minimal fake replaces it.
        { provide: ThemeService, useValue: { preference: signal('system') } },
      ],
    });
    component = TestBed.createComponent(BrandPage).componentInstance;
  });

  it('joins the two brands in alphabetical order', () => {
    component.brandName.set('Kaged');

    expect(component.comparisonPairSlug('Ghost')).toBe('ghost-vs-kaged');
  });

  it('produces the same canonical URL whichever brand is "current"', () => {
    component.brandName.set('ghost');
    const a = component.comparisonPairSlug('kaged');

    component.brandName.set('kaged');
    const b = component.comparisonPairSlug('ghost');

    expect(a).toBe(b);
  });
});
