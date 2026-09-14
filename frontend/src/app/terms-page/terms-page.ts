import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

/**
 * Terms of use. Shares the privacy page's layout and styles: the legal pages
 * should read as one set.
 */
@Component({
  selector: 'app-terms-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './terms-page.html',
  styleUrl: '../privacy-policy-page/privacy-policy-page.css',
})
export class TermsPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Terms of Use | ${SITE_NAME}`,
      description: `The terms for using ${SITE_NAME}: how prices and nutrition facts are collected, affiliate links, and what the site is not.`,
      canonicalPath: '/terms',
    });
  }
}
