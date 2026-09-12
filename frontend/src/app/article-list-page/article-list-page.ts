import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ArticleSummary } from '../core/article.model';
import { ArticlesService } from '../core/articles.service';
import { MARKET } from '../core/market';
import { PageMetaService } from '../core/page-meta.service';
import { normalizeSearchText } from '../core/search-normalize';
import { SITE_NAME } from '../core/site-identity';
import { SiteHeader } from '../site-header/site-header';

type GuideTone = 'violet' | 'blue' | 'mint' | 'rose' | 'orange';

interface LearningPathConfig {
  title: string;
  description: string;
  iconClass: string;
  tone: GuideTone;
  articleSlugs: string[];
}

interface LearningPath extends LearningPathConfig {
  articles: ArticleSummary[];
}

// A path only shows the articles that exist; one whose slugs are all
// missing is dropped, so an unpublished guide never becomes a dead link.
const LEARNING_PATH_CONFIGS: LearningPathConfig[] = [
  {
    title: "I'm just starting out",
    description: 'Learn the basics and pick the right products.',
    iconClass: 'ph-star',
    tone: 'violet',
    articleSlugs: ['how-to-choose-whey-protein', 'creatine-what-to-know'],
  },
  {
    title: 'I want to perform better',
    description: 'Support your training performance.',
    iconClass: 'ph-lightning',
    tone: 'blue',
    articleSlugs: ['how-to-choose-a-pre-workout', 'bcaa-vs-eaa', 'electrolytes-explained'],
  },
  {
    title: 'I want to understand weight management',
    description: 'Fat loss, gaining mass and what supplements can and can\'t do.',
    iconClass: 'ph-fire',
    tone: 'mint',
    articleSlugs: ['do-fat-burners-work', 'how-to-use-a-mass-gainer'],
  },
];

@Component({
  selector: 'app-article-list-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './article-list-page.html',
})
export class ArticleListPage implements OnInit {
  private readonly articlesService = inject(ArticlesService);
  private readonly pageMeta = inject(PageMetaService);

  protected readonly articles = signal<ArticleSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly searchQuery = signal('');

  protected readonly hasSearch = computed(() => this.searchQuery().trim().length > 0);
  protected readonly featuredArticles = computed(() => this.articles().slice(0, 3));
  protected readonly topicColumns = computed(() => {
    const remaining = this.articles().slice(3);
    const middle = Math.ceil(remaining.length / 2);
    return [remaining.slice(0, middle), remaining.slice(middle)];
  });
  protected readonly filteredArticles = computed(() => {
    const query = normalizeSearchText(this.searchQuery()).trim();
    if (!query) return this.articles();

    return this.articles().filter((article) =>
      normalizeSearchText(`${article.title} ${article.summary} ${article.slug}`).includes(query),
    );
  });
  protected readonly learningPaths = computed<LearningPath[]>(() => {
    const articlesBySlug = new Map(this.articles().map((article) => [article.slug, article]));

    return LEARNING_PATH_CONFIGS.map((path) => ({
      ...path,
      articles: path.articleSlugs
        .map((slug) => articlesBySlug.get(slug))
        .filter((article): article is ArticleSummary => article !== undefined),
    })).filter((path) => path.articles.length > 0);
  });

  ngOnInit(): void {
    this.pageMeta.set({
      // "Guides" alone matches no search intent; the title and H1 both carry
      // the topic.
      title: `Supplement Guides | ${SITE_NAME}`,
      description: 'Informational guides on protein powder, creatine, pre-workout and other supplements: honest, independent help choosing what to buy.',
      canonicalPath: '/guides',
    });

    this.articlesService.getArticles().subscribe({
      next: (articles) => {
        this.articles.set(articles);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  protected setSearchQuery(value: string): void {
    this.searchQuery.set(value);
  }

  protected clearSearch(): void {
    this.searchQuery.set('');
  }

  protected formatArticleDate(value: string): string {
    return new Intl.DateTimeFormat(MARKET.locale, {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: MARKET.timeZone,
    }).format(new Date(value));
  }

  protected articleIconClass(article: ArticleSummary): string {
    const slug = article.slug;
    if (slug.includes('pre-workout')) return 'ph-lightning';
    if (slug.includes('creatine')) return 'ph-barbell';
    if (slug.includes('gainer')) return 'ph-barbell';
    if (slug.includes('protein-bar')) return 'ph-cookie';
    if (slug.includes('protein') || slug.includes('whey')) return 'ph-jar';
    if (slug.includes('vitamin')) return 'ph-shield-plus';
    if (slug.includes('electrolyte')) return 'ph-drop';
    if (slug.includes('bcaa') || slug.includes('eaa')) return 'ph-share-network';
    if (slug.includes('fat-burner')) return 'ph-fire';
    return 'ph-book-open-text';
  }

  protected articleTone(article: ArticleSummary): GuideTone {
    const slug = article.slug;
    if (slug.includes('pre-workout')) return 'rose';
    if (slug.includes('protein-bar') || slug.includes('vitamin')) return 'orange';
    if (slug.includes('protein') || slug.includes('whey')) return 'mint';
    if (slug.includes('fat-burner')) return 'mint';
    if (slug.includes('bcaa') || slug.includes('eaa') || slug.includes('electrolyte')) return 'blue';
    return 'violet';
  }
}
