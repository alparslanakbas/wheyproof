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
});
