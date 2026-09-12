import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-how-it-works-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './how-it-works-page.html',
  styleUrl: './how-it-works-page.css',
})
export class HowItWorksPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `How It Works | ${SITE_NAME}`,
      description: `How ${SITE_NAME} collects prices, how a "real discount" is calculated, and whether affiliate links affect the ranking, explained plainly.`,
      canonicalPath: '/how-it-works',
    });
  }
}
