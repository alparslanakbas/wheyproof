import { DOCUMENT } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ACTIVITY_OPTIONS, BodyCalculator, findBodyCalculator } from '../core/body-calculators';
import { MARKET } from '../core/market';
import { showNotFound } from '../core/not-found-navigation';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { SiteHeader } from '../site-header/site-header';

const KG_PER_LB = 0.45359237;
const CM_PER_INCH = 2.54;

type UnitSystem = 'us' | 'metric';

// ONE component for calories (TDEE), BMI and water intake: which inputs show
// and the calculation itself come from configuration (body-calculators.ts).
//
// The formulas work in metric. US visitors enter feet/inches and pounds by
// default; the conversion happens here, at the edge, so the formulas and
// their tests stay in one unit system.
@Component({
  selector: 'app-body-calculator-page',
  imports: [FormsModule, RouterLink, SiteHeader],
  templateUrl: './body-calculator-page.html',
})
export class BodyCalculatorPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private structuredDataEl: HTMLScriptElement | null = null;

  protected readonly activityOptions = ACTIVITY_OPTIONS;
  protected readonly config = signal<BodyCalculator | null>(null);

  protected readonly units = signal<UnitSystem>('us');
  protected readonly gender = signal<'male' | 'female'>('male');
  protected readonly age = signal<number | null>(null);
  protected readonly heightCm = signal<number | null>(null);
  protected readonly heightFt = signal<number | null>(null);
  protected readonly heightIn = signal<number | null>(null);
  // In lb or kg, depending on the unit system.
  protected readonly weight = signal<number | null>(null);
  protected readonly activityId = signal<string>('moderate');

  private readonly metricHeight = computed(() => {
    if (this.units() === 'metric') return this.heightCm();
    const feet = this.heightFt();
    if (!feet || feet <= 0) return null;
    return (feet * 12 + (this.heightIn() ?? 0)) * CM_PER_INCH;
  });

  private readonly metricWeight = computed(() => {
    const value = this.weight();
    if (!value || value <= 0) return null;
    return this.units() === 'us' ? value * KG_PER_LB : value;
  });

  // Null while an input is missing or out of a sensible range: filling in a
  // default and saying "this is your result" would mislead.
  protected readonly result = computed(() => {
    const cfg = this.config();
    if (!cfg) return null;

    return cfg.calculate({
      gender: this.gender(),
      age: this.age(),
      height: this.metricHeight(),
      weight: this.metricWeight(),
      activityId: this.activityId(),
    });
  });

  protected showsField(field: string): boolean {
    return this.config()?.fields.includes(field as never) ?? false;
  }

  // Converts what was already typed, so switching units keeps the result.
  protected setUnits(units: UnitSystem): void {
    if (units === this.units()) return;

    const heightCm = this.metricHeight();
    const weightKg = this.metricWeight();

    if (units === 'metric') {
      this.heightCm.set(heightCm ? Math.round(heightCm) : null);
      this.weight.set(weightKg ? Math.round(weightKg * 10) / 10 : null);
    } else {
      if (heightCm) {
        const totalInches = Math.round(heightCm / CM_PER_INCH);
        this.heightFt.set(Math.floor(totalInches / 12));
        this.heightIn.set(totalInches % 12);
      } else {
        this.heightFt.set(null);
        this.heightIn.set(null);
      }
      this.weight.set(weightKg ? Math.round(weightKg / KG_PER_LB) : null);
    }

    this.units.set(units);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const config = findBodyCalculator(params.get('slug') ?? '');
      if (!config) {
        showNotFound(this.router);
        return;
      }

      this.config.set(config);
      this.pageMeta.set({
        title: config.title,
        description: config.description,
        canonicalPath: `/calculators/${config.slug}`,
      });

      this.structuredDataEl = upsertJsonLdScript(this.document, this.structuredDataEl, {
        '@context': 'https://schema.org',
        '@type': 'WebApplication',
        name: config.h1,
        applicationCategory: 'HealthApplication',
        operatingSystem: 'Web',
        offers: { '@type': 'Offer', price: '0', priceCurrency: MARKET.currency },
      });
    });
  }
}
