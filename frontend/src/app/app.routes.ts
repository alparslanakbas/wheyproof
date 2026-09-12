import { Routes, UrlSegment } from '@angular/router';

import { DealsList } from './deals-list/deals-list';
import { BODY_CALCULATORS } from './core/body-calculators';

// The home page (DealsList) stays eager on purpose: it is the first SSR
// request and the most visited route, and lazy loading would add a round
// trip to the first load. Every other page is lazy; PreloadAllModules (see
// app.config.ts) fetches them in the background once the home page is idle,
// so a click does not wait on the network.
export const routes: Routes = [
  { path: '', component: DealsList },
  { path: 'product/:id', component: DealsList },
  { path: 'product/:id/:slug', component: DealsList },
  {
    path: 'brand/:brandSlug',
    loadComponent: () => import('./brand-page/brand-page').then((m) => m.BrandPage),
  },
  // Brand x category pages, for searches such as "transparent labs protein
  // powder".
  {
    path: 'brand/:brandSlug/:categorySlug',
    loadComponent: () => import('./brand-page/brand-page').then((m) => m.BrandPage),
  },
  {
    path: 'categories',
    loadComponent: () => import('./category-list-page/category-list-page').then((m) => m.CategoryListPage),
  },
  {
    path: 'brands',
    loadComponent: () => import('./brand-list-page/brand-list-page').then((m) => m.BrandListPage),
  },
  {
    path: 'category/:categorySlug',
    loadComponent: () => import('./category-page/category-page').then((m) => m.CategoryPage),
  },
  {
    path: 'privacy',
    loadComponent: () => import('./privacy-policy-page/privacy-policy-page').then((m) => m.PrivacyPolicyPage),
  },
  {
    path: 'cookies',
    loadComponent: () => import('./cookie-policy-page/cookie-policy-page').then((m) => m.CookiePolicyPage),
  },
  {
    path: 'guides',
    loadComponent: () => import('./article-list-page/article-list-page').then((m) => m.ArticleListPage),
  },
  {
    path: 'guides/:slug',
    loadComponent: () => import('./article-page/article-page').then((m) => m.ArticlePage),
  },
  {
    path: 'how-it-works',
    loadComponent: () => import('./how-it-works-page/how-it-works-page').then((m) => m.HowItWorksPage),
  },
  {
    path: 'about',
    loadComponent: () => import('./about-page/about-page').then((m) => m.AboutPage),
  },
  {
    path: 'contact',
    loadComponent: () => import('./contact-page/contact-page').then((m) => m.ContactPage),
  },
  {
    path: 'glossary',
    loadComponent: () => import('./glossary-page/glossary-page').then((m) => m.GlossaryPage),
  },
  {
    path: 'review/:id/:slug',
    loadComponent: () => import('./product-review-page/product-review-page').then((m) => m.ProductReviewPage),
  },
  {
    path: 'calculators',
    loadComponent: () => import('./calculator-list-page/calculator-list-page').then((m) => m.CalculatorListPage),
  },
  // ORDER MATTERS: the specific calculator routes come before the generic
  // 'calculators/:slug' below.
  {
    path: 'calculators/protein',
    loadComponent: () => import('./protein-calculator-page/protein-calculator-page').then((m) => m.ProteinCalculatorPage),
  },
  // Body calculators (calories/TDEE, BMI, water) are generated from config,
  // so adding a tool does not touch this file. The slug reaches the component
  // as a ROUTE PARAMETER (':slug'); passing it through `data` on a fixed path
  // was tried and never arrived.
  ...BODY_CALCULATORS.map((calc) => ({
    matcher: (segments: UrlSegment[]) =>
      segments.length === 2 && segments[0].path === 'calculators' && segments[1].path === calc.slug
        ? { consumed: segments, posParams: { slug: segments[1] } }
        : null,
    loadComponent: () => import('./body-calculator-page/body-calculator-page').then((m) => m.BodyCalculatorPage),
  })),
  // Supplement dose and cost calculators (creatine, beta-alanine, citrulline,
  // EAA) share one component, configured per slug. Generic, so LAST: an
  // unknown slug is caught here and sent back to /calculators.
  {
    path: 'calculators/:slug',
    loadComponent: () => import('./supplement-dosage-page/supplement-dosage-page').then((m) => m.SupplementDosagePage),
  },
  {
    path: 'watchlist',
    loadComponent: () => import('./favorites-page/favorites-page').then((m) => m.FavoritesPage),
  },
  {
    path: 'compare/:pair',
    loadComponent: () => import('./brand-comparison-page/brand-comparison-page').then((m) => m.BrandComparisonPage),
  },
  // Product comparison has its own address, separate from brand comparison.
  {
    path: 'compare-products/:pair',
    loadComponent: () => import('./product-comparison-page/product-comparison-page').then((m) => m.ProductComparisonPage),
  },
  // Admin panel. Linked from nowhere, absent from the sitemap and noindex.
  // The real protection is Cloudflare Access in front of it and the session
  // cookie behind it; these only keep it out of search results.
  {
    path: 'admin',
    loadComponent: () => import('./admin-page/admin-page').then((m) => m.AdminPage),
  },
  // Pages whose content is missing arrive here with `skipLocationChange`
  // (see core/not-found-navigation.ts): the address bar keeps the requested
  // URL, only the rendered component changes.
  {
    path: 'not-found',
    loadComponent: () => import('./not-found-page/not-found-page').then((m) => m.NotFoundPage),
  },
  // MUST STAY LAST: the catch-all lets no later route match. Without it an
  // unmatched URL fell through to Express's bare "Cannot GET /..." page.
  {
    path: '**',
    loadComponent: () => import('./not-found-page/not-found-page').then((m) => m.NotFoundPage),
  },
];
