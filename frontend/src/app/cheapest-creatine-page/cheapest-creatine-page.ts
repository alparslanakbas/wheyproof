import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { FaqItem } from '../core/category-faqs';
import { MARKET } from '../core/market';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { GoalPickSection } from '../core/supplement-goals';
import { PRICE_UNIT } from '../core/unit-price';
import { SiteHeader } from '../site-header/site-header';
import { ValuePickList } from '../value-pick-list/value-pick-list';

const PAGE_PATH = '/cheapest-creatine';
const IMPERIAL = MARKET.measurement === 'imperial';

// Ranking and scope live in one place on the backend: ValuePickRanker's creatine rule (100 g to 2 kg, blends and
// multi-product bundles out, one product per brand). The "Which supplement?" pages show the same list with 6 products.
const CREATINE_LIST: GoalPickSection = {
  id: 'ranking',
  category: 'creatine',
  title: `Ranked by price per ${PRICE_UNIT.word}`,
  allLink: { label: 'All creatine prices', path: '/category/creatine' },
};

// Price questions only. Questions about creatine itself (what it does, side effects) are on the category page;
// repeating them here would make the two pages compete for the same searches.
const FAQS: FaqItem[] = [
  {
    question: 'Is a bigger tub always cheaper?',
    answer:
      'Not always. A small tub on sale can cost less per ' +
      PRICE_UNIT.word +
      ' than a large tub at full price, which is why the ranking looks at the price per ' +
      PRICE_UNIT.word +
      ', not the tub size.',
  },
  {
    question: 'How much does creatine cost per day?',
    answer: IMPERIAL
      ? '3–5 g a day is the most common range. At 5 g a day a pound lasts about 90 days, so divide the price per ' +
        'pound by 90 for a rough daily cost: creatine at $20 a pound is about 22 cents a day.'
      : '3–5 g a day is the most common range. At 5 g a day a kilogram lasts 200 days, so divide the price per ' +
        'kilogram by 200 for a rough daily cost: creatine at £20 a kilogram is 10p a day.',
  },
  {
    question: 'How often are prices updated?',
    answer:
      'We check most stores every six hours and a few once a day; the ranking is recalculated after every check. ' +
      'Discount codes applied at checkout are not included.',
  },
  {
    question: 'What do the green labels mean?',
    answer:
      "Both come from our own price history, not the store's crossed-out price. \"Real discount\" means today's " +
      'price is below the price we saw on at least 7 different days in the last 30. "Lowest in 30 days" is the ' +
      'lowest price in our last 30 days of history.',
  },
];

/**
 * Permanent list page: the cheapest creatine by price per pound (US) or kilogram (UK). Mirror of the Turkish
 * site's /en-ucuz-kreatin (2026-10-07); only the language, unit and currency differ.
 */
@Component({
  selector: 'app-cheapest-creatine-page',
  imports: [RouterLink, SiteHeader, ValuePickList],
  templateUrl: './cheapest-creatine-page.html',
})
export class CheapestCreatinePage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  protected readonly list = CREATINE_LIST;
  protected readonly faqs = FAQS;
  protected readonly unitWord = PRICE_UNIT.word;
  protected readonly imperial = IMPERIAL;

  ngOnInit(): void {
    const year = new Date().getFullYear();
    const word = PRICE_UNIT.word;
    this.pageMeta.set({
      title: `Cheapest Creatine ${year}: Price per ${word[0].toUpperCase()}${word.slice(1)} | ${SITE_NAME}`,
      description:
        `Creatine prices compared per ${word}: the cheapest creatine in ${year}, the best-value tub from each brand. ` +
        'Prices are checked every day.',
      canonicalPath: PAGE_PATH,
    });
    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Creatine', path: '/category/creatine' },
        { name: 'Cheapest creatine', path: PAGE_PATH },
      ]),
    );
    upsertJsonLdScript(this.document, null, {
      '@context': 'https://schema.org',
      '@type': 'FAQPage',
      mainEntity: FAQS.map((f) => ({
        '@type': 'Question',
        name: f.question,
        acceptedAnswer: { '@type': 'Answer', text: f.answer },
      })),
    });
  }
}
