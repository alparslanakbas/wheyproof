import { Component, OnInit, PLATFORM_ID, RESPONSE_INIT, inject } from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { RouterLink } from '@angular/router';

import { PageMetaService } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

/**
 * Not found page (404).
 *
 * Without a catch-all (`**`) route, an unmatched URL fell through to the SSR
 * layer and Express printed its bare default ("Cannot GET /..."): a 1 kB
 * error with no title, no menu and no way back into the site.
 *
 * <b>THE STATUS CODE MUST BE 404.</b> On the server the component sets it
 * through `RESPONSE_INIT`. Otherwise the page would return 200 and Google
 * would count it as a "soft 404", a valid-page signal for missing content.
 * The review page uses the same mechanism for 503.
 *
 * It is also `noIndex`: a 404 is never indexed anyway, but in a single-page
 * app a visitor can reach this page client-side, where there is no HTTP code.
 */
@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './not-found-page.html',
  styleUrl: './not-found-page.css',
})
export class NotFoundPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });
  private readonly isServer = isPlatformServer(inject(PLATFORM_ID));

  ngOnInit(): void {
    if (this.isServer && this.responseInit) {
      this.responseInit.status = 404;
    }

    this.pageMeta.set({
      title: `Page not found | ${SITE_NAME}`,
      description:
        "The page you were looking for doesn't exist. Continue from the categories, the brands or the search box.",
      // Not its OWN address as canonical: this page appears at many
      // addresses, and declaring each one canonical would report duplicates.
      canonicalPath: '/',
      noIndex: true,
    });
  }
}
