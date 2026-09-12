import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { GLOSSARY } from '../core/glossary';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { slugify } from '../core/slugify';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-glossary-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './glossary-page.html',
})
export class GlossaryPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  // Section anchors are slugs: a heading with spaces makes an awkward id.
  protected readonly groups = GLOSSARY.map((group) => ({ ...group, anchor: slugify(group.heading) }));

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Supplement Glossary | ${SITE_NAME}`,
      description:
        'Short, honest definitions of supplement terms like BCAA, EAA, creatine monohydrate, whey isolate and bioavailability.',
      canonicalPath: '/glossary',
    });

    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Glossary', path: '/glossary' },
      ]),
    );

    // DefinedTermSet: the structured data type Google expects for glossary
    // and definition pages.
    upsertJsonLdScript(this.document, null, {
      '@context': 'https://schema.org',
      '@type': 'DefinedTermSet',
      name: `${SITE_NAME} Supplement Glossary`,
      hasDefinedTerm: this.groups.flatMap((group) =>
        group.terms.map((t) => ({
          '@type': 'DefinedTerm',
          name: t.term,
          description: t.definition,
        })),
      ),
    });
  }
}
