import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { CURRENT_EDITION } from '../core/editions';
import { PageMetaService } from '../core/page-meta.service';
import { FOUNDER, SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-privacy-policy-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './privacy-policy-page.html',
  styleUrl: './privacy-policy-page.css',
})
export class PrivacyPolicyPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);

  // The UK section adds what the UK GDPR requires on top of the shared text:
  // the named controller, a legal basis for each use, transfer safeguards and
  // the right to complain to the ICO.
  protected readonly isUk = CURRENT_EDITION.code === 'UK';
  protected readonly founderName = FOUNDER.name;

  /** Section numbers after the UK-only "Legal Basis" section shift by one there. */
  protected sectionNumber(usNumber: number): string {
    return String(this.isUk ? usNumber + 1 : usNumber).padStart(2, '0');
  }

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Privacy Policy | ${SITE_NAME}`,
      description: `What data ${SITE_NAME} collects, how it's used and what your rights are, explained plainly.`,
      canonicalPath: '/privacy',
    });
  }
}
