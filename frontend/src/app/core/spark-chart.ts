import { PricePoint } from './price-history.model';

// Shared, pure coordinate/path math for product-modal.ts's full price chart
// and the home page's small sparklines. Depends on no DI or component
// instance: it takes (points, min, max, size) and returns SVG coordinates or
// path strings.
export interface SparkChartDimensions {
  width: number;
  height: number;
  paddingY: number;
}

export function toCoordinates(
  points: PricePoint[],
  min: number,
  max: number,
  { width, height, paddingY }: SparkChartDimensions,
): [number, number][] {
  if (points.length === 0) return [];

  const times = points.map((p) => new Date(p.scrapedAt).getTime());
  const minTime = Math.min(...times);
  const maxTime = Math.max(...times);
  const timeSpan = maxTime - minTime || 1;

  // With a price that never changed (min === max) the line stuck to the
  // bottom and looked "empty"; the visual range is widened around the price
  // so the line sits in the middle.
  let effectiveMin = min;
  let effectiveMax = max;
  if (max - min < 0.01) {
    const padding = Math.max(max * 0.05, 1);
    effectiveMin = min - padding;
    effectiveMax = max + padding;
  }
  const priceSpan = effectiveMax - effectiveMin;

  return points.map((p) => {
    const t = new Date(p.scrapedAt).getTime();
    const x = points.length === 1 ? width / 2 : ((t - minTime) / timeSpan) * width;
    const y = height - paddingY - ((p.price - effectiveMin) / priceSpan) * (height - paddingY * 2);
    return [x, y] as [number, number];
  });
}

export function buildLinePath(coords: [number, number][]): string {
  if (coords.length === 0) return '';
  if (coords.length === 1) {
    const [[x, y]] = coords;
    return `M ${x - 4} ${y} L ${x + 4} ${y}`;
  }
  return coords.map(([x, y], i) => `${i === 0 ? 'M' : 'L'} ${x} ${y}`).join(' ');
}

export function buildAreaPath(coords: [number, number][], height: number): string {
  if (coords.length === 0) return '';
  const first = coords[0];
  const last = coords[coords.length - 1];
  const line = coords.map(([x, y], i) => `${i === 0 ? 'M' : 'L'} ${x} ${y}`).join(' ');
  return `${line} L ${last[0]} ${height} L ${first[0]} ${height} Z`;
}
