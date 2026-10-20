import { Deal } from './deal.model';

/** A product that is really cheaper by the season-page rule (backend CampaignDiscount). */
export interface CampaignDiscount {
  product: Deal;
  priorLowest: number;
  realPercent: number;
  discountStart: string;
}

/**
 * The products stores show with a crossed-out "was" price, classified against the lowest price in the 30 days
 * before the discount started. The rule lives in one place on the backend (CampaignDiscountService).
 */
export interface CampaignSummary {
  measuredAt: string;
  storeSaysSale: number;
  real: number;
  notCheaper: number;
  permanent: number;
  insufficientData: number;
  realDiscounts: CampaignDiscount[];
}

/**
 * Black Friday: the day after Thanksgiving, the fourth Thursday of November (UTC midnight; months start at 0).
 * Not "the last Friday of November" (the Turkish site's Efsane Cuma rule): when November has five Thursdays the
 * two differ, e.g. 2029: Black Friday is the 23rd, the last Friday the 30th.
 */
export function blackFriday(year: number): Date {
  const firstThursday = 1 + ((4 - new Date(Date.UTC(year, 10, 1)).getUTCDay() + 7) % 7);
  return new Date(Date.UTC(year, 10, firstThursday + 22));
}

/** Cyber Monday: the Monday after Black Friday. */
export function cyberMonday(year: number): Date {
  const friday = blackFriday(year);
  return new Date(Date.UTC(year, friday.getUTCMonth(), friday.getUTCDate() + 3));
}
