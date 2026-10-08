import { DOCUMENT } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

/**
 * Who wheyproofbot is, for the people running the stores it reads. Its User-Agent links here, and
 * Shopify's bot registration asks for such a page (purpose, how to opt out, contact). The values
 * shown must match what the backend sends: WebBotAuth:UserAgent and WebBotAuth:SignatureAgent in
 * appsettings.Production.json, RobotsTxtHandler.ProductToken.
 */
@Component({
  selector: 'app-bot-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './bot-page.html',
})
export class BotPage implements OnInit {
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);

  protected readonly siteName = SITE_NAME;
  protected readonly userAgent = 'Mozilla/5.0 (compatible; wheyproofbot/1.0; +https://www.wheyproof.com/bot)';
  protected readonly signatureAgent = 'https://api.wheyproof.com';
  protected readonly keyDirectory = 'https://api.wheyproof.com/.well-known/http-message-signatures-directory';
  protected readonly contactEmail = 'support@wheyproof.com';

  ngOnInit(): void {
    this.pageMeta.set({
      title: `wheyproofbot: our crawler | ${SITE_NAME}`,
      description:
        'What wheyproofbot fetches from supplement stores, how often, how it identifies itself, how to block it with robots.txt and how to reach us.',
      canonicalPath: '/bot',
    });

    upsertJsonLdScript(
      this.document,
      null,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'wheyproofbot', path: '/bot' },
      ]),
    );
  }
}
