import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BODY_CALCULATORS } from '../core/body-calculators';
import { calculatorPhosphorIcon } from '../core/nav-icons';
import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SUPPLEMENT_DOSAGES } from '../core/supplement-dosages';
import { SiteHeader } from '../site-header/site-header';

interface CalculatorCard {
  path: string;
  title: string;
  description: string;
  iconClass: string;
  tone: CalculatorTone;
}

type CalculatorSection = 'body' | 'dosage';
type CalculatorTone = 'violet' | 'mint' | 'blue' | 'cyan' | 'orange' | 'rose';

// Index page of the calculators, in the same pattern as /categories. The
// nav's "Calculators" dropdown links here and to each tool.
@Component({
  selector: 'app-calculator-list-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './calculator-list-page.html',
})
export class CalculatorListPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  protected readonly activeSection = signal<CalculatorSection>('body');

  // Nutrition/body calculators in one group, supplement dosage calculators
  // in another. Icons are shared in core/nav-icons.ts (the same set as the
  // nav dropdowns).
  protected readonly bodyGroupCalculators: CalculatorCard[] = [
    {
      path: '/calculators/protein',
      title: 'Daily Protein Needs',
      description:
        'Work out your daily protein target from body weight and training load, and see the best value products per serving.',
      iconClass: calculatorPhosphorIcon('protein'),
      tone: 'violet',
    },
    ...BODY_CALCULATORS.map((c, index): CalculatorCard => ({
      path: `/calculators/${c.slug}`,
      title: c.name,
      description: c.description,
      iconClass: calculatorPhosphorIcon(c.slug),
      tone: (['mint', 'blue', 'cyan'] as CalculatorTone[])[index] ?? 'violet',
    })),
  ];

  protected readonly dosageGroupCalculators: CalculatorCard[] = SUPPLEMENT_DOSAGES.map((s, index): CalculatorCard => ({
    path: `/calculators/${s.slug}`,
    title: `${s.name} Dosage`,
    description: `Common range: ${s.minDailyGrams}-${s.maxDailyGrams} g a day. See how long a package lasts and what it costs per day.`,
    iconClass: calculatorPhosphorIcon(s.slug),
    tone: (['violet', 'mint', 'blue', 'orange', 'rose'] as CalculatorTone[])[index] ?? 'violet',
  }));

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Supplement Calculators | ${SITE_NAME}`,
      description:
        'Protein needs plus creatine, beta-alanine, citrulline, betaine and EAA dosage calculators, with results tied to current product prices.',
      canonicalPath: '/calculators',
    });
  }

  protected selectSection(section: CalculatorSection): void {
    this.activeSection.set(section);
    const target = this.document.getElementById(section === 'body' ? 'nutrition-tools' : 'dosage-tools');
    target?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
