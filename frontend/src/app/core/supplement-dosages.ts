import { SITE_NAME } from './site-identity';

export interface SupplementDosage {
  slug: string;
  // Page and heading text
  name: string;
  title: string;
  description: string;
  h1: string;
  intro: string;
  // Daily dose range in grams. These supplements are NOT dosed by body
  // weight; research and practice use fixed ranges. A "dose by body weight"
  // tool would be made up.
  minDailyGrams: number;
  maxDailyGrams: number;
  defaultDailyGrams: number;
  // What the dose rests on, stated plainly so it doesn't look invented.
  dosageNote: string;
  // Category + search term used to fetch products. The category alone isn't
  // enough (beta-alanine sits in "amino-acids", but not every amino product
  // is beta-alanine).
  // May be NULL: then only the search term filters.
  category: string | null;
  // May be NULL: when the category is exactly the product group (creatine),
  // a search term only narrows it and drops products spelled differently.
  searchTerm: string | null;
  // Related guide (when there is one), for internal linking.
  guideSlug?: string;
  guideLabel?: string;
}

export const SUPPLEMENT_DOSAGES: SupplementDosage[] = [
  {
    slug: 'creatine-dosage',
    name: 'Creatine',
    title: `Creatine Dosage Calculator: How Many Grams a Day? | ${SITE_NAME}`,
    description:
      'Work out your daily creatine dose and how long a tub will last. See the cost per day at current prices.',
    h1: 'Creatine Dosage Calculator',
    intro:
      'Creatine isn\'t dosed by body weight; the common intake is a fixed range. Pick your daily dose below and see how many days a tub lasts and what it costs per day at real prices.',
    minDailyGrams: 3,
    maxDailyGrams: 5,
    defaultDailyGrams: 5,
    dosageNote:
      '3-5 grams a day is the most common range for creatine, and most products come with a 5-gram scoop. More doesn\'t speed up saturation; the excess is excreted. Taking it every day matters more than the amount.',
    category: 'creatine',
    // The category is exactly creatine products; a search term would only
    // drop products whose names spell it differently.
    searchTerm: null,
    guideSlug: 'creatine-what-to-know',
    guideLabel: 'Creatine: What to Know Before You Buy',
  },
  {
    slug: 'beta-alanine-dosage',
    name: 'Beta-Alanine',
    title: `Beta-Alanine Dosage Calculator: How Many Grams a Day? | ${SITE_NAME}`,
    description:
      'Work out your daily beta-alanine dose and how long a tub will last. See the cost per day at current prices.',
    h1: 'Beta-Alanine Dosage Calculator',
    intro:
      'Beta-alanine is taken in a fixed range, not by body weight. Pick your daily dose and see how long a tub lasts at real prices.',
    minDailyGrams: 3,
    maxDailyGrams: 6,
    defaultDailyGrams: 3.2,
    dosageNote:
      '3-6 grams a day is the commonly used range. Like creatine, it works by building up over time; a single dose isn\'t expected to do much. The skin tingling (paresthesia) is a harmless, temporary effect of this ingredient; splitting the dose can reduce it.',
    category: 'amino-acids',
    searchTerm: 'alanine',
  },
  {
    slug: 'citrulline-dosage',
    name: 'Citrulline',
    title: `Citrulline Dosage Calculator | ${SITE_NAME}`,
    description:
      'Work out your daily citrulline dose and how long a tub will last. See the cost per day at current prices.',
    h1: 'Citrulline Dosage Calculator',
    intro:
      'Citrulline isn\'t dosed by body weight. Pick your daily dose below and see how many days a tub lasts and what it costs per day at real prices.',
    minDailyGrams: 3,
    maxDailyGrams: 8,
    defaultDailyGrams: 6,
    dosageNote:
      '3-6 grams for pure L-citrulline and 6-8 grams for citrulline malate are the commonly used ranges; the label says which form a product contains, and the two aren\'t the same amount. Taking it about an hour before training is a common choice.',
    category: 'amino-acids',
    searchTerm: 'citrulline',
  },
  {
    slug: 'betaine-dosage',
    name: 'Betaine',
    title: `Betaine Dosage Calculator | ${SITE_NAME}`,
    description:
      'Work out your daily betaine dose and how long a tub will last. See the cost per day at current prices.',
    h1: 'Betaine Dosage Calculator',
    intro:
      'Betaine is taken in a fixed range, not calculated by body weight. Pick your daily dose and see how long a tub lasts and what it costs per day.',
    minDailyGrams: 1.25,
    maxDailyGrams: 2.5,
    defaultDailyGrams: 2.5,
    dosageNote:
      '1.25-2.5 grams a day (betaine anhydrous) is the range most used in studies. It also occurs naturally in foods such as beets. As with creatine, its effect relies on regular use. Note: "betaine HCl" is a different product (a digestive aid); the form used for performance is betaine anhydrous.',
    category: null,
    searchTerm: 'betaine',
  },
  {
    slug: 'eaa-dosage',
    name: 'EAA',
    title: `EAA Dosage Calculator: How Many Grams a Day? | ${SITE_NAME}`,
    description:
      'Work out your daily EAA dose and how long a tub will last. See the cost per day at current prices.',
    h1: 'EAA Dosage Calculator',
    intro:
      'EAAs are taken per serving, not by body weight. Pick your daily dose and see how long a tub lasts at real prices.',
    minDailyGrams: 5,
    maxDailyGrams: 15,
    defaultDailyGrams: 10,
    dosageNote:
      '5-15 grams per serving is the common range. One important note: if you already meet your daily protein target, there is no strong evidence that extra EAAs add benefit; whey protein is already rich in essential amino acids.',
    category: 'amino-acids',
    searchTerm: 'eaa',
    guideSlug: 'bcaa-vs-eaa',
    guideLabel: 'BCAA vs EAA',
  },
];

export function findSupplementDosage(slug: string): SupplementDosage | undefined {
  return SUPPLEMENT_DOSAGES.find((s) => s.slug === slug);
}
