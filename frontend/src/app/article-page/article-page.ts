import { DOCUMENT, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { Article } from '../core/article.model';
import { ArticlesService } from '../core/articles.service';
import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { canonicalOrigin } from '../core/canonical-link';
import { MARKET } from '../core/market';
import { showNotFound } from '../core/not-found-navigation';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { FOUNDER, SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

@Component({
  selector: 'app-article-page',
  imports: [RouterLink, DatePipe, SiteHeader],
  templateUrl: './article-page.html',
})
export class ArticlePage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly articlesService = inject(ArticlesService);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private structuredDataEl: HTMLScriptElement | null = null;
  private breadcrumbEl: HTMLScriptElement | null = null;

  protected readonly article = signal<Article | null>(null);
  protected readonly loading = signal(true);
  protected readonly timeZone = MARKET.timeZone;

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const slug = params.get('slug') ?? '';
      this.loadArticle(slug);
    });
  }

  private loadArticle(slug: string): void {
    this.loading.set(true);

    this.articlesService.getArticleBySlug(slug).subscribe({
      next: (article) => {
        this.article.set(article);
        this.setMeta(article);
        this.loading.set(false);
      },
      // A missing guide is a real 404 (redirecting it to the list would be a
      // soft 404). A temporary error falls back to the list instead of
      // claiming the page is gone.
      error: (err: HttpErrorResponse) => {
        if (err.status === 404) {
          showNotFound(this.router);
          return;
        }
        void this.router.navigate(['/guides']);
      },
    });
  }

  private setMeta(article: Article): void {
    const title = `${article.title} | ${SITE_NAME}`;

    this.pageMeta.set({
      title,
      description: article.summary,
      canonicalPath: `/guides/${article.slug}`,
      ogType: 'article',
      ogImage: article.coverImageUrl ?? undefined,
    });

    // A real Person as author (an E-E-A-T signal); see core/site-identity.ts.
    const jsonLd = {
      '@context': 'https://schema.org',
      '@type': 'Article',
      headline: article.title,
      description: article.summary,
      datePublished: article.publishedAt,
      ...(article.coverImageUrl ? { image: article.coverImageUrl } : {}),
      author: { '@type': 'Person', name: FOUNDER.name, url: FOUNDER.blogUrl },
      publisher: { '@type': 'Organization', name: SITE_NAME },
      mainEntityOfPage: `${canonicalOrigin(this.document)}/guides/${article.slug}`,
    };

    this.structuredDataEl = upsertJsonLdScript(this.document, this.structuredDataEl, jsonLd);

    this.breadcrumbEl = upsertJsonLdScript(
      this.document,
      this.breadcrumbEl,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Guides', path: '/guides' },
        { name: article.title, path: `/guides/${article.slug}` },
      ]),
    );
  }
}
