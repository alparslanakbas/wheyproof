import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { FOUNDER, SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-about-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './about-page.html',
  styleUrl: './about-page.css',
})
export class AboutPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  protected readonly founder = FOUNDER;
  protected readonly siteName = SITE_NAME;

  ngOnInit(): void {
    this.pageMeta.set({
      title: `About Us | ${SITE_NAME}`,
      description: `Who built ${SITE_NAME} and why: founder ${FOUNDER.name} and an approach based on real price history.`,
      canonicalPath: '/about',
    });

    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'About Us', path: '/about' },
      ]),
    );
  }
}
