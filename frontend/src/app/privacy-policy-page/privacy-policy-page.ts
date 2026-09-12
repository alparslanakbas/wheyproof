import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-privacy-policy-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './privacy-policy-page.html',
  styleUrl: './privacy-policy-page.css',
})
export class PrivacyPolicyPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Privacy Policy | ${SITE_NAME}`,
      description: `What data ${SITE_NAME} collects, how it's used and what your rights are, explained plainly.`,
      canonicalPath: '/privacy',
    });
  }
}
