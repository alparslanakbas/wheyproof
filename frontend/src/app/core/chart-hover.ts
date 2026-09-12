import { MARKET } from './market';
import { PricePoint } from './price-history.model';

/**
 * Shared logic for mouse/touch interaction on price charts.
 *
 * WHY A SEPARATE FILE: these calculations (nearest point, edge alignment of
 * the tooltip, labels for points on the same day) caused THREE production
 * bugs in the product modal: repeated axis labels, a clipped tooltip at the
 * edge, and repeated same-day/same-price points on hover. Copying them into
 * the review page's chart would have brought the same bugs back in a copy.
 *
 * Chart dimensions are parameters: the modal is 600×220 with a grid, the
 * review page lower and plainer. The math is shared, the look is not.
 */

// A fixed zone (MARKET.timeZone) rather than the device zone, or the same
// moment could show a different "day" to different visitors.
const dayFormatter = new Intl.DateTimeFormat(MARKET.locale, {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: MARKET.timeZone,
});

// When the price really changed within a day, the date alone is not enough;
// the time shows when it changed.
const dayTimeFormatter = new Intl.DateTimeFormat(MARKET.locale, {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
  timeZone: MARKET.timeZone,
});

export type HoverAlign = 'left' | 'center' | 'right';

/**
 * Index of the data point nearest to the pointer's screen coordinate. The SVG
 * stretches with `preserveAspectRatio="none"`, so screen width has to be
 * scaled to the viewBox width.
 */
export function nearestPointIndex(
  svg: SVGSVGElement,
  clientX: number,
  coords: readonly [number, number][],
  viewBoxWidth: number,
): number | null {
  if (coords.length === 0) return null;

  const rect = svg.getBoundingClientRect();
  if (rect.width === 0) return null;

  const svgX = (clientX - rect.left) * (viewBoxWidth / rect.width);

  let nearestIndex = 0;
  let nearestDist = Infinity;
  coords.forEach(([x], i) => {
    const dist = Math.abs(x - svgX);
    if (dist < nearestDist) {
      nearestDist = dist;
      nearestIndex = i;
    }
  });
  return nearestIndex;
}

/**
 * Tooltip alignment. Centered near an edge, half the tooltip overflowed and
 * was clipped, so near an edge it leans that way.
 */
export function hoverAlign(x: number, viewBoxWidth: number): HoverAlign {
  if (x < viewBoxWidth * 0.12) return 'left';
  if (x > viewBoxWidth * 0.88) return 'right';
  return 'center';
}

/** Tooltip date; adds the time when several points fall on the same day. */
export function tooltipDateLabel(points: readonly PricePoint[], idx: number): string {
  const point = points[idx];
  if (!point) return '';
  const day = dayFormatter.format(new Date(point.scrapedAt));
  const sameDayCount = points.filter(
    (p) => dayFormatter.format(new Date(p.scrapedAt)) === day,
  ).length;
  const formatter = sameDayCount > 1 ? dayTimeFormatter : dayFormatter;
  return formatter.format(new Date(point.scrapedAt));
}

/**
 * Collapses consecutive points with the same day and the same price.
 *
 * Prices are checked several times a day, so an unchanged price piles up many
 * points per day, and hovering showed the same date over and over. A price
 * that really changed within the day keeps both points.
 */
export function dedupeSameDaySamePrice(points: readonly PricePoint[]): PricePoint[] {
  const result: PricePoint[] = [];

  for (const point of points) {
    const prev = result[result.length - 1];
    const sameDay =
      prev &&
      dayFormatter.format(new Date(prev.scrapedAt)) ===
        dayFormatter.format(new Date(point.scrapedAt));

    if (prev && sameDay && prev.price === point.price) {
      result[result.length - 1] = point;
    } else {
      result.push(point);
    }
  }

  return result;
}
