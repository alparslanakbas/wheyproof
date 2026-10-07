import { upsertJsonLdScript } from './page-meta.service';

describe('upsertJsonLdScript', () => {
  // A scraped product name containing "</script>" closed the tag in SSR output
  // and ran the rest on the page, with the admin's session since the panel
  // shares the origin (security review, 2026-09-25).
  it('keeps a </script> in a product name from closing the tag', () => {
    const name = 'X </script><script>alert(1)</script>';

    const el = upsertJsonLdScript(document, null, { name });

    expect(document.head.innerHTML).not.toContain('</script><script>');
    expect(el.textContent).not.toContain('<');
    expect(JSON.parse(el.textContent!).name).toBe(name);
    el.remove();
  });

  // Hydration (2026-10-07): the component is rebuilt in the browser without a reference and a second block was
  // appended next to the server-rendered one (every block twice on the live site). First call = server, second = client.
  describe('same-type block', () => {
    const blocks = () => [...document.head.querySelectorAll('script[type="application/ld+json"]')];
    afterEach(() => blocks().forEach((b) => b.remove()));

    it('is updated in place instead of appended again, even without a reference', () => {
      const server = upsertJsonLdScript(document, null, { '@type': 'BreadcrumbList', name: 'old' });
      const client = upsertJsonLdScript(document, null, { '@type': 'BreadcrumbList', name: 'new' });

      expect(client).toBe(server);
      expect(blocks()).toHaveLength(1);
      expect(JSON.parse(client.textContent!).name).toBe('new');
    });

    it('keeps different types as separate blocks', () => {
      upsertJsonLdScript(document, null, { '@type': 'BreadcrumbList' });
      upsertJsonLdScript(document, null, { '@type': 'FAQPage' });

      expect(blocks().map((b) => b.getAttribute('data-ld'))).toEqual(['BreadcrumbList', 'FAQPage']);
    });

    it('still creates a new block per call for data without a type', () => {
      upsertJsonLdScript(document, null, [{ '@type': 'Product' }]);
      upsertJsonLdScript(document, null, [{ '@type': 'Product' }]);

      expect(blocks()).toHaveLength(2);
    });

    it('replaces a removed block with a new one', () => {
      upsertJsonLdScript(document, null, { '@type': 'Product' }).remove();

      const fresh = upsertJsonLdScript(document, null, { '@type': 'Product' });

      expect(fresh.isConnected).toBe(true);
      expect(blocks()).toHaveLength(1);
    });
  });
});
