import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-cookie-policy-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './cookie-policy-page.html',
  styleUrl: './cookie-policy-page.css',
})
export class CookiePolicyPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Cookie Policy | ${SITE_NAME}`,
      description: `Which cookies and local storage ${SITE_NAME} uses, and when we'd ask for your permission, explained plainly.`,
      canonicalPath: '/cookies',
    });
  }
}
