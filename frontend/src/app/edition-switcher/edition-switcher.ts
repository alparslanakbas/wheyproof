import { Component, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { CURRENT_EDITION, Edition, editionHref, listedEditions } from '../core/editions';

/**
 * Country switcher: links the current page to its counterpart in the other
 * country editions (see core/editions.ts for which pages have one).
 *
 * Two forms. "header" is a compact menu, visible on every screen size so
 * visitors find it where sites usually put it. "footer" is a plain row of
 * links: the header menu's links only exist while it is open, so they aren't
 * in the server-rendered HTML, and the footer row is what crawlers follow to
 * the other editions.
 *
 * No flag emoji: Windows browsers don't draw them and show the letters "US".
 * Renders nothing while fewer than two editions are listed.
 */
@Component({
  selector: 'app-edition-switcher',
  template: `
    @if (editions.length > 0) {
      @if (variant() === 'header') {
        <div class="relative">
          <button
            type="button"
            class="flex h-10 items-center gap-1.5 rounded-[9px] border border-white/15 px-3 text-sm font-medium text-[#e3e5f0] hover:border-[#8879f6]/70"
            [attr.aria-expanded]="open()"
            [attr.aria-label]="'Country: ' + current.label + '. Choose another country'"
            (click)="open.set(!open())"
          >
            <i class="ph ph-globe-simple text-[17px]" aria-hidden="true"></i>{{ current.code }}
            <i class="ph ph-caret-down text-xs transition-transform" [class.rotate-180]="open()" aria-hidden="true"></i>
          </button>
          @if (open()) {
            <div class="fixed inset-0 z-40" (click)="open.set(false)"></div>
            <div class="absolute right-0 top-full z-50 mt-3 w-56 rounded-[14px] border border-[var(--noc-divider)] bg-[var(--noc-card)] p-2 text-[var(--noc-neutral-900)] shadow-[var(--noc-shadow-lg)] dark:text-[var(--noc-text)]">
              @for (edition of editions; track edition.code) {
                @if (edition.code === current.code) {
                  <span aria-current="true" class="flex items-center justify-between rounded-[9px] px-3 py-2.5 text-sm font-semibold text-[var(--noc-accent)]">
                    {{ edition.label }}<i class="ph ph-check" aria-hidden="true"></i>
                  </span>
                } @else {
                  <a [href]="href(edition)" [attr.hreflang]="edition.hreflang" class="block rounded-[9px] px-3 py-2.5 text-sm hover:bg-[color-mix(in_srgb,var(--noc-accent)_8%,transparent)] hover:text-[var(--noc-accent)]">
                    {{ edition.label }}
                  </a>
                }
              }
            </div>
          }
        </div>
      } @else {
        <nav class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-[#858ca9]" aria-label="Country">
          <i class="ph ph-globe-simple text-sm" aria-hidden="true"></i>
          @for (edition of editions; track edition.code) {
            @if (edition.code === current.code) {
              <span aria-current="true" class="font-semibold text-[#aeb4ce]">{{ edition.label }}</span>
            } @else {
              <a [href]="href(edition)" [attr.hreflang]="edition.hreflang" class="hover:text-white">{{ edition.label }}</a>
            }
          }
        </nav>
      }
    }
  `,
})
export class EditionSwitcher {
  readonly variant = input<'header' | 'footer'>('header');

  private readonly router = inject(Router);

  // The Router's URL has no base href ("/category/creatine"), which is the
  // form editionHref expects.
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  protected readonly editions = listedEditions();
  protected readonly current = CURRENT_EDITION;
  protected readonly open = signal(false);

  protected href(edition: Edition): string {
    return editionHref(edition, this.url());
  }
}
