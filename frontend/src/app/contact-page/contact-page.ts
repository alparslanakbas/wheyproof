import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { FOUNDER, SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-contact-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './contact-page.html',
  styleUrl: './contact-page.css',
})
export class ContactPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  protected readonly founder = FOUNDER;

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Contact | ${SITE_NAME}`,
      description: `Contact ${SITE_NAME}: a direct email address for wrong prices, brand partnerships, advertising and press.`,
      canonicalPath: '/contact',
    });

    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Contact', path: '/contact' },
      ]),
    );
  }
}
