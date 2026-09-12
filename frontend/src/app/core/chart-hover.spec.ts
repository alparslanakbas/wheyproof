import { dedupeSameDaySamePrice, hoverAlign, nearestPointIndex, tooltipDateLabel } from './chart-hover';

// These calculations caused THREE production bugs in the product modal:
// repeated same-day axis labels, a clipped tooltip at the edge, and
// same-day/same-price points repeating on hover. The review page's chart
// reuses this module rather than a copy, and the tests live here: pure
// functions, no TestBed needed.
describe('chart-hover', () => {
  describe('dedupeSameDaySamePrice', () => {
    it('collapses consecutive same-day, same-price points into one', () => {
      const points = [
        { price: 39.99, scrapedAt: '2026-08-10T14:00:00Z' },
        { price: 39.99, scrapedAt: '2026-08-10T20:00:00Z' },
        { price: 44.99, scrapedAt: '2026-08-11T14:00:00Z' },
        { price: 44.99, scrapedAt: '2026-08-12T14:00:00Z' },
      ];

      const result = dedupeSameDaySamePrice(points);

      expect(result).toEqual([
        { price: 39.99, scrapedAt: '2026-08-10T20:00:00Z' },
        { price: 44.99, scrapedAt: '2026-08-11T14:00:00Z' },
        { price: 44.99, scrapedAt: '2026-08-12T14:00:00Z' },
      ]);
    });

    it('keeps same-day points with different prices (a real change within the day)', () => {
      const points = [
        { price: 100, scrapedAt: '2026-08-10T14:00:00Z' },
        { price: 90, scrapedAt: '2026-08-10T20:00:00Z' },
      ];

      const result = dedupeSameDaySamePrice(points);

      expect(result.length).toBe(2);
    });
  });

  describe('tooltipDateLabel', () => {
    it('shows only the date when the day has one point (no time)', () => {
      const points = [{ price: 100, scrapedAt: '2026-08-11T14:00:00Z' }];

      const label = tooltipDateLabel(points, 0);

      expect(label).not.toMatch(/\d{1,2}:\d{2}/);
    });

    it('adds the time when the day has several points, so the moment of change is clear', () => {
      // 12:00Z and 20:00Z are 8 AM and 4 PM the same day in New York.
      const points = [
        { price: 100, scrapedAt: '2026-08-11T12:00:00Z' },
        { price: 90, scrapedAt: '2026-08-11T20:00:00Z' },
      ];

      const label = tooltipDateLabel(points, 1);

      expect(label).toMatch(/\d{1,2}:\d{2}\s[AP]M$/);
    });
  });

  describe('hoverAlign', () => {
    // Centered near an edge, half the tooltip overflowed the container and
    // got clipped (clearly visible on mobile).
    it('leans a point near the left edge to the left', () => {
      expect(hoverAlign(10, 600)).toBe('left');
    });

    it('leans a point near the right edge to the right', () => {
      expect(hoverAlign(590, 600)).toBe('right');
    });

    it('centers a point in the middle', () => {
      expect(hoverAlign(300, 600)).toBe('center');
    });
  });

  describe('nearestPointIndex', () => {
    // The SVG stretches with preserveAspectRatio="none": the screen width can
    // differ from the viewBox width, and without scaling the cursor snaps to
    // the wrong point.
    const fakeSvg = (left: number, width: number) =>
      ({ getBoundingClientRect: () => ({ left, width }) }) as unknown as SVGSVGElement;

    const coords: [number, number][] = [
      [0, 10],
      [300, 20],
      [600, 30],
    ];

    it('scales correctly when the screen width differs from the viewBox width', () => {
      // A 600-unit viewBox drawn 300px wide: 150px on screen is 300 units in
      // the viewBox, i.e. the middle point.
      expect(nearestPointIndex(fakeSvg(0, 300), 150, coords, 600)).toBe(1);
    });

    it("accounts for the container's left offset", () => {
      expect(nearestPointIndex(fakeSvg(100, 600), 105, coords, 600)).toBe(0);
    });

    it('returns null when there are no points', () => {
      expect(nearestPointIndex(fakeSvg(0, 600), 100, [], 600)).toBeNull();
    });

    it("returns null for an SVG not measured yet (width 0) instead of dividing by zero", () => {
      expect(nearestPointIndex(fakeSvg(0, 0), 100, coords, 600)).toBeNull();
    });
  });
});
