import { FaqItem } from './category-faqs';
import { formatPrice } from './market';

export interface BrandFaqInput {
  brandName: string;
  couponCodes: string[];
  totalProducts: number | null;
  averageDiscountPercent: number | null;
  topCategoryLabel: string | null;
}

// "{brand} coupon code" searches were the largest query group landing on the
// Turkish site's brand pages, yet the page was a product list: the body said
// "coupon" three times and had no heading about coupons. Visitors didn't find
// what they searched for, and search engines matched the page weakly.
//
// These questions fill that gap. Two rules:
// 1. NO invented coupons. When we have no code, we say so: meeting an expired
//    code at checkout is worse than having no code at all.
// 2. Answers don't speculate about the brand (we don't know its sale
//    calendar); they rest ONLY on our own data.
export interface BrandCategoryFaqInput {
  brandName: string;
  categoryLabel: string;
  productCount: number | null;
  averagePrice: number | null;
  categoryAveragePrice: number | null;
  averageDiscountPercent: number | null;
}

// Brand x category pages ("transparent labs creatine"). These pages were
// 150-270 words, almost only a product list, which kept them ranking low.
//
// The questions are SEPARATE from the category page's (the same text on two
// pages weakens both) and specific to the intersection: the brand's price
// position in that category, product count and discount depth. All from our
// own data, no assumptions about the brand.
export function buildBrandCategoryFaqs(input: BrandCategoryFaqInput): FaqItem[] {
  const { brandName, categoryLabel, productCount, averagePrice, categoryAveragePrice, averageDiscountPercent } = input;
  const lower = categoryLabel.toLowerCase();
  const faqs: FaqItem[] = [];

  // The page's most original content: the brand's price position in the category.
  if (averagePrice && categoryAveragePrice) {
    const diff = Math.round(Math.abs(averagePrice - categoryAveragePrice) / categoryAveragePrice * 100);
    const cheaper = averagePrice < categoryAveragePrice;
    const caveat =
      'An average alone isn\'t a complete measure: package sizes differ, so the cost per serving gives a more accurate comparison.';
    faqs.push({
      question: `Is ${brandName} ${lower} expensive?`,
      answer: diff < 3
        ? `The ${brandName} ${lower} products we track average ${formatPrice(averagePrice)}; the category as a whole averages ${formatPrice(categoryAveragePrice)}. So the brand sits very close to the average here. ${caveat}`
        : `The ${brandName} ${lower} products we track average ${formatPrice(averagePrice)}; the category as a whole averages ${formatPrice(categoryAveragePrice)}, so the brand is about ${diff}% ${cheaper ? 'cheaper' : 'more expensive'} than average. ${caveat}`,
    });
  }

  if (productCount) {
    faqs.push({
      question: `How many ${brandName} ${lower} products do you track?`,
      answer: `We currently track ${productCount} ${lower} products from ${brandName} and record each price four times a day. When the brand removes a product from its catalog we stop listing it, but we keep the price history it built up.`,
    });
  }

  faqs.push({
    question: `How often does ${brandName} ${lower} go on sale?`,
    answer: averageDiscountPercent
      ? `The price drops we currently verify in this category average ${averageDiscountPercent}%. That isn't a fixed promotion but a snapshot recalculated on every check; it changes as prices change.`
      : 'We don\'t see a verified price drop in this category right now. That doesn\'t mean the brand never runs sales, only that no real drop has formed in the price history we collect yet. Add products to your watchlist to hear when the price falls.',
  });

  faqs.push({
    question: `Are these ${lower} prices up to date?`,
    answer:
      'We collect prices from the store\'s own site four times a day, and every product card shows when it was last checked. The final price is the one at the store\'s checkout, where shipping, promotion terms or cart discounts can change the details.',
  });

  return faqs;
}

export function buildBrandFaqs(input: BrandFaqInput): FaqItem[] {
  const { brandName, couponCodes, totalProducts, averageDiscountPercent, topCategoryLabel } = input;
  const hasCoupon = couponCodes.length > 0;

  const couponAnswer = hasCoupon
    ? `Yes. We currently have ${couponCodes.length === 1 ? 'one verified code' : `${couponCodes.length} verified codes`} for ${brandName}: ${couponCodes.join(', ')}. Codes aren't collected automatically; we check each one by hand and remove it when it expires. The brand's terms can still change at checkout.`
    : `We don't have an active, verified coupon code for ${brandName} right now. When we can't find one, we don't list made-up codes: trying an expired code at checkout is more annoying than having none. Instead, you can follow the brand's real price drops on this page; a well-timed purchase often saves more than a code would.`;

  const trackingAnswer = totalProducts
    ? `We check ${totalProducts} products from ${brandName} four times a day and record the price on every check, so we see a price drop without waiting for the brand to announce it.`
    : `We check ${brandName} products four times a day and record the price on every check, so we see a price drop without waiting for the brand to announce it.`;

  const topCategory = topCategoryLabel ? `The category where we track the most ${brandName} products is ${topCategoryLabel.toLowerCase()}.` : '';
  const depthAnswer = averageDiscountPercent
    ? `The price drops we currently verify for ${brandName} average ${averageDiscountPercent}%. ${topCategory} This is recalculated on every check; it is the real situation at that moment, not a fixed promotion.`.replace(/\s+/g, ' ').trim()
    : `We don't see a verified price drop for ${brandName} right now. That doesn't mean the brand never runs sales, only that no real drop has formed in the price history we collect yet. ${topCategory}`.trim();

  return [
    {
      question: `Is there a ${brandName} coupon code?`,
      answer: couponAnswer,
    },
    {
      question: `How can I buy ${brandName} for less without a coupon code?`,
      answer:
        'A product\'s price doesn\'t stay fixed all year. The "Lowest in 30 days" label shows the product is at its cheapest point of the last month right now, usually the most sensible moment to buy. If you are not in a hurry, add it to your watchlist and get notified when the price drops.',
    },
    {
      question: 'Are the discounts here the brand\'s own sales?',
      answer:
        'The "Real price drops" figures come from the price history we collect: a product counts as discounted when its current price is below the highest price we saw in the last 30 days. "Store sales" show the old and new prices the store displays on its own site; we don\'t verify those and label them separately. We keep the two apart on purpose.',
    },
    {
      question: `How often are ${brandName} prices updated?`,
      answer: trackingAnswer,
    },
    {
      question: `How deep do ${brandName} discounts go?`,
      answer: depthAnswer,
    },
    {
      question: 'Why do coupon codes look different on every site?',
      answer:
        'Most coupon sites collect codes automatically and never remove expired ones, so you see dozens of conflicting "codes" for the same brand. We only list codes we have checked by hand, and when we can\'t verify one we leave the page without a code rather than fill it.',
    },
  ];
}
