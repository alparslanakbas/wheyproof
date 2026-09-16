import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

import { brandSlug } from './app/core/brand-slug';
import { API_BASE_URL } from './app/core/api.config';
import { INTERNAL_API_HEADERS, toInternalApiUrl } from './app/core/internal-api';
import { slugify } from './app/core/slugify';
import { BODY_CALCULATORS } from './app/core/body-calculators';
import { SUPPLEMENT_DOSAGES } from './app/core/supplement-dosages';

const browserDistFolder = join(import.meta.dirname, '../browser');

// The sitemap and /go also reach the API over the Docker network (see core/internal-api.ts).
const INTERNAL_API_BASE = process.env['API_INTERNAL_URL'] || null;
const apiFetch = (path: string, init: RequestInit = {}) =>
  fetch(toInternalApiUrl(`${API_BASE_URL}${path}`, INTERNAL_API_BASE), {
    ...init,
    headers: { ...(INTERNAL_API_BASE ? INTERNAL_API_HEADERS : {}), ...(init.headers as Record<string, string> | undefined) },
  });

// The sitemap and robots.txt always use this fixed origin, never the
// request's Host header: built from the Host, any other hostname that reaches
// this server would announce its own sitemap, and crawlers can index that
// copy regardless of the canonical <link>.
const CANONICAL_HOST = 'www.wheyproof.com';
const CANONICAL_ORIGIN = `https://${CANONICAL_HOST}`;

const app = express();
// TLS ends at the proxy in front of us and requests arrive as plain HTTP;
// without this req.protocol is always "http" (X-Forwarded-Proto ignored).
app.set('trust proxy', true);

// Every request from a host OTHER than the canonical one gets a noindex
// header: not just the sitemap and robots, but every page Angular SSR
// renders. The goal is that no second copy of the site gets indexed.
app.use((req, res, next) => {
  if ((req.hostname || '').toLowerCase() !== CANONICAL_HOST) {
    res.setHeader('X-Robots-Tag', 'noindex, nofollow');
  }
  next();
});

const angularApp = new AngularNodeAppEngine();

interface SitemapEntry {
  id: number;
  name: string;
  // When the content last really changed (NOT the last scrape); see
  // Product.ContentUpdatedAt.
  lastModifiedAt: string;
  hasReviewContent: boolean;
}

interface FilterOptions {
  brands: string[];
  categories: string[];
}

interface ArticleSitemapEntry {
  slug: string;
  publishedAt: string;
}

interface BrandCategoryPair {
  brandName: string;
  category: string;
  productCount: number;
}

// A brand × category page enters the sitemap only with enough products. A
// page with 1-2 products is "thin content" to Google, exactly what we want
// to avoid. The page itself stays reachable; it just isn't offered for
// crawling.
const MIN_PRODUCTS_FOR_SITEMAP = 3;

/** Minimum total products for a brand's comparison pages: a category
 *  average needs enough products to mean anything. A small brand's
 *  "average" often rests on a single product. The pages keep working;
 *  they just aren't offered for crawling. */
const MIN_PRODUCTS_FOR_COMPARISON = 40;

// The sitemap grows with the catalog, so it can't be a static file: the raw
// data comes from the backend (/api/products/sitemap) and becomes XML here.
app.get('/sitemap.xml', async (req, res) => {
  const origin = CANONICAL_ORIGIN;

  try {
    const [productsResponse, filtersResponse, articlesResponse, pairsResponse] = await Promise.all([
      apiFetch('/api/products/sitemap'),
      apiFetch('/api/filters'),
      apiFetch('/api/articles'),
      apiFetch('/api/brand-category-pairs'),
    ]);
    const products = (await productsResponse.json()) as SitemapEntry[];
    const filters = (await filtersResponse.json()) as FilterOptions;
    const articles = (await articlesResponse.json()) as ArticleSitemapEntry[];
    const brandCategoryPairs = (await pairsResponse.json()) as BrandCategoryPair[];

    const productUrls = products
      .map(
        (p) =>
          `<url><loc>${origin}/product/${p.id}/${slugify(p.name)}</loc><lastmod>${new Date(p.lastModifiedAt).toISOString()}</lastmod><changefreq>daily</changefreq><priority>0.8</priority></url>`,
      )
      .join('');

    // Review pages: only products with a real content source (brand
    // description or nutrition table; see backend HasReviewContent). The
    // page is open for other products too, but not offered as thin content.
    const reviewUrls = products
      .filter((p) => p.hasReviewContent)
      .map(
        (p) =>
          `<url><loc>${origin}/review/${p.id}/${slugify(p.name)}</loc><lastmod>${new Date(p.lastModifiedAt).toISOString()}</lastmod><changefreq>weekly</changefreq><priority>0.6</priority></url>`,
      )
      .join('');

    // Brand pages, aimed at "[brand] coupon code" searches.
    const brandUrls = filters.brands
      .map(
        (brand) =>
          `<url><loc>${origin}/brand/${brandSlug(brand)}</loc><changefreq>daily</changefreq><priority>0.9</priority></url>`,
      )
      .join('');

    // Brand × category intersections, for searches like "optimum nutrition
    // protein powder price". Only pairs with enough products; the list comes
    // from the backend and needs no hand maintenance.
    const brandCategoryUrls = brandCategoryPairs
      .filter((pair) => pair.productCount >= MIN_PRODUCTS_FOR_SITEMAP)
      .map(
        (pair) =>
          `<url><loc>${origin}/brand/${brandSlug(pair.brandName)}/${pair.category}</loc><changefreq>daily</changefreq><priority>0.7</priority></url>`,
      )
      .join('');

    // Category pages, plus /categories, the index listing all of them.
    const categoryUrls =
      `<url><loc>${origin}/categories</loc><changefreq>weekly</changefreq><priority>0.6</priority></url>` +
      filters.categories
        .map(
          (category) =>
            `<url><loc>${origin}/category/${category}</loc><changefreq>daily</changefreq><priority>0.8</priority></url>`,
        )
        .join('');

    const legalUrls =
      `<url><loc>${origin}/privacy</loc><changefreq>monthly</changefreq><priority>0.3</priority></url>` +
      `<url><loc>${origin}/cookies</loc><changefreq>monthly</changefreq><priority>0.3</priority></url>` +
      `<url><loc>${origin}/terms</loc><changefreq>monthly</changefreq><priority>0.3</priority></url>` +
      `<url><loc>${origin}/how-it-works</loc><changefreq>monthly</changefreq><priority>0.5</priority></url>` +
      `<url><loc>${origin}/about</loc><changefreq>monthly</changefreq><priority>0.5</priority></url>` +
      `<url><loc>${origin}/contact</loc><changefreq>monthly</changefreq><priority>0.4</priority></url>` +
      `<url><loc>${origin}/glossary</loc><changefreq>monthly</changefreq><priority>0.6</priority></url>` +
      // Calculators: each targets its own search ("creatine dosage
      // calculator"), plus the index page.
      `<url><loc>${origin}/calculators</loc><changefreq>weekly</changefreq><priority>0.6</priority></url>` +
      `<url><loc>${origin}/calculators/protein</loc><changefreq>weekly</changefreq><priority>0.7</priority></url>` +
      SUPPLEMENT_DOSAGES.map(
        (s) => `<url><loc>${origin}/calculators/${s.slug}</loc><changefreq>weekly</changefreq><priority>0.7</priority></url>`,
      ).join('') +
      BODY_CALCULATORS.map(
        (c) => `<url><loc>${origin}/calculators/${c.slug}</loc><changefreq>monthly</changefreq><priority>0.7</priority></url>`,
      ).join('');

    // Brand comparison pages: every brand pair, in alphabetical order (the
    // same canonical rule as brand-comparison-page.ts), one URL per pair.
    //
    // Only brands with enough products: these pages compare AVERAGE prices
    // per category, and a brand with a single product in a category would
    // present that one price as the "brand average", a number that looks
    // like a statistic but isn't.
    const productCountByBrand = new Map<string, number>();
    for (const pair of brandCategoryPairs) {
      const key = brandSlug(pair.brandName);
      productCountByBrand.set(key, (productCountByBrand.get(key) ?? 0) + pair.productCount);
    }

    const comparisonPairs: string[] = [];
    const sortedBrands = [...filters.brands]
      .map((b) => brandSlug(b))
      .filter((b) => (productCountByBrand.get(b) ?? 0) >= MIN_PRODUCTS_FOR_COMPARISON)
      .sort();
    for (let i = 0; i < sortedBrands.length; i++) {
      for (let j = i + 1; j < sortedBrands.length; j++) {
        comparisonPairs.push(`${sortedBrands[i]}-vs-${sortedBrands[j]}`);
      }
    }
    const comparisonUrls = comparisonPairs
      .map((pair) => `<url><loc>${origin}/compare/${pair}</loc><changefreq>weekly</changefreq><priority>0.6</priority></url>`)
      .join('');

    // Guides: informational content, as important as product and category
    // pages (0.7).
    const articleUrls =
      `<url><loc>${origin}/guides</loc><changefreq>weekly</changefreq><priority>0.6</priority></url>` +
      articles
        .map(
          (a) =>
            `<url><loc>${origin}/guides/${a.slug}</loc><lastmod>${new Date(a.publishedAt).toISOString()}</lastmod><changefreq>monthly</changefreq><priority>0.7</priority></url>`,
        )
        .join('');

    const xml =
      `<?xml version="1.0" encoding="UTF-8"?>` +
      `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">` +
      `<url><loc>${origin}/</loc><changefreq>hourly</changefreq><priority>1.0</priority></url>` +
      brandUrls +
      brandCategoryUrls +
      categoryUrls +
      productUrls +
      reviewUrls +
      legalUrls +
      articleUrls +
      comparisonUrls +
      `</urlset>`;

    res.set('Content-Type', 'application/xml');
    res.send(xml);
  } catch (error) {
    console.error('sitemap.xml could not be built:', error);
    res.status(502).send('The sitemap is unavailable right now.');
  }
});

// The store link is relative (/go/:id), so visitors click an address on the
// domain they see rather than an unfamiliar API host (careful users read
// that as a phishing link). This proxies the backend's real /go/{id} on the
// server and passes the same 302 through; the backend click counter keeps
// working. Search engine bots are recognized by user agent: not exact, but
// the goal is keeping the click counter close to real people, not security.
const BOT_USER_AGENT = /bot|crawler|spider|crawling|bingpreview|slurp|duckduck|yandex|baidu|facebookexternalhit|embedly|quora|pinterest|whatsapp|telegram/i;

app.get('/go/:id', async (req, res) => {
  // /go/{id} is not a content page but an endpoint that 302s away. A robots
  // Disallow doesn't keep an address OUT of the index, it only stops the
  // content being read: a search engine that learns the address elsewhere
  // indexes it with "no description available" (this happened on the
  // Turkish site, where the redirect outranked the home page for a brand
  // search). The right signal is this header: the bot can fetch the page,
  // sees "noindex" and drops the address. That's why robots.txt has no
  // Disallow for it; otherwise the bot would never see this header.
  res.set('X-Robots-Tag', 'noindex, nofollow');

  const isBot = BOT_USER_AGENT.test(req.get('user-agent') ?? '');

  try {
    const response = await apiFetch(`/go/${req.params.id}`, {
      redirect: 'manual',
      // No click count for bots: the counter feeds the click report shared
      // with brands, and bot traffic would make it misleading.
      headers: isBot ? { 'X-Bot-Request': '1' } : undefined,
    });
    const location = response.headers.get('location');
    res.redirect(302, location ?? '/');
  } catch (error) {
    console.error('/go/:id redirect failed:', error);
    res.redirect(302, '/');
  }
});

app.get('/robots.txt', (req, res) => {
  res.set('Content-Type', 'text/plain');
  // A request from any host other than the canonical one gets a different
  // robots.txt: that copy isn't open to crawling at all and announces no
  // sitemap. Together with the X-Robots-Tag header above that's two
  // independent signals (one stops crawling, one stops indexing).
  if ((req.hostname || '').toLowerCase() !== CANONICAL_HOST) {
    res.send('User-agent: *\nDisallow: /\n');
    return;
  }
  // DELIBERATELY no Disallow for /go/{id}; see the note on that route.
  //
  // /cdn-cgi/ is Cloudflare's own injected path: its email obfuscation turns
  // mailto: links into /cdn-cgi/l/email-protection, which a bot following
  // without JavaScript gets a 404 from. Here Disallow IS the right tool:
  // the goal is that bots never request the path, and it isn't our content.
  res.send(
    `User-agent: *\nAllow: /\nDisallow: /cdn-cgi/\n\nSitemap: ${CANONICAL_ORIGIN}/sitemap.xml\n`,
  );
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * A short-lived in-memory cache for server-rendered pages.
 *
 * Most of the home page's time to first byte is Angular's rendering (product
 * cards, charts, FAQs), not waiting on the backend. The TTL matches the
 * backend's output cache so the two layers don't serve data of different
 * freshness.
 *
 * Deliberate limits:
 * - Only GET, and only HTML that returned 200. Redirects (old product URLs,
 *   canonical slugs) and error responses are never cached; serving a
 *   temporary 503 to everyone for a minute would mislead search engines.
 * - Nothing personal: the watchlist page and requests carrying a recovery
 *   link stay out.
 * - A response that sets a cookie is not cached.
 */
const SSR_CACHE_TTL_MS = 60_000;
// Pages are ~0.5 MB, so the cap is deliberately low: roughly a 70 MB upper
// bound. With a 60-second TTL a bigger cap wouldn't raise the hit rate; its
// real job is stopping a crawler that walks thousands of URLs from filling
// memory.
const SSR_CACHE_MAX_ENTRIES = 120;
/** One huge page mustn't fill the cache; anything above this isn't stored. */
const SSR_CACHE_MAX_BYTES = 1_000_000;

interface SsrCacheEntry {
  expiresAt: number;
  status: number;
  headers: [string, string][];
  body: string;
}

/** Insertion order is kept, so the oldest entry is always the first key. */
const ssrCache = new Map<string, SsrCacheEntry>();

function ssrCacheKey(req: express.Request): string | null {
  if (req.method !== 'GET') return null;
  const path = req.path;
  // The watchlist depends on the key in the browser; a recovery link carries
  // a token that belongs to one person.
  if (path.startsWith('/watchlist')) return null;
  if (req.query['recover'] !== undefined) return null;
  return req.originalUrl;
}

app.use((req, res, next) => {
  const key = ssrCacheKey(req);

  if (key) {
    const hit = ssrCache.get(key);
    if (hit && hit.expiresAt > Date.now()) {
      // Move the most recently used entry to the end so eviction order stays right.
      ssrCache.delete(key);
      ssrCache.set(key, hit);
      for (const [name, value] of hit.headers) res.setHeader(name, value);
      res.setHeader('X-SSR-Cache', 'HIT');
      res.status(hit.status).send(hit.body);
      return;
    }
    if (hit) ssrCache.delete(key);
  }

  angularApp
    .handle(req)
    .then(async (response) => {
      if (!response) return next();

      // Angular turns router.navigate() calls during rendering into 302
      // (temporary) redirects. Our canonical redirects are PERMANENT: a
      // product's right address is always the slugged one. A 302 tells
      // Google "keep the old address, keep checking", so those pages pile up
      // under "Page with redirect" and validation keeps failing.
      //
      // Only redirects to a product address become 301s. Ones that land on
      // the home page are OUT of scope: they mean "no such product", not a
      // permanent move; a 301 would tell Google "this product is now the
      // home page".
      const location = response.headers.get('location');
      if (response.status === 302 && location && new URL(location, 'https://x').pathname.startsWith('/product/')) {
        response = new Response(response.body, {
          status: 301,
          statusText: 'Moved Permanently',
          headers: response.headers,
        });
      }

      const contentType = response.headers.get('content-type') ?? '';
      const setsCookie = response.headers.has('set-cookie');
      const cacheable =
        key !== null && response.status === 200 && contentType.includes('text/html') && !setsCookie;

      if (!cacheable) {
        return writeResponseToNodeResponse(response, res);
      }

      // We read the body ourselves, so we also write the response by hand;
      // writeResponseToNodeResponse consumes the stream and leaves nothing.
      const body = await response.text();
      const headers: [string, string][] = [];
      response.headers.forEach((value, name) => {
        // Length and encoding headers go stale the moment we write the body
        // ourselves; copying them led to a Content-Length that didn't match
        // the body. Express computes the right one.
        const lower = name.toLowerCase();
        if (lower === 'content-length' || lower === 'content-encoding' || lower === 'transfer-encoding') {
          return;
        }
        headers.push([name, value]);
      });

      if (Buffer.byteLength(body) <= SSR_CACHE_MAX_BYTES) {
        if (ssrCache.size >= SSR_CACHE_MAX_ENTRIES) {
          const oldest = ssrCache.keys().next().value;
          if (oldest !== undefined) ssrCache.delete(oldest);
        }
        ssrCache.set(key, {
          expiresAt: Date.now() + SSR_CACHE_TTL_MS,
          status: response.status,
          headers,
          body,
        });
      }

      for (const [name, value] of headers) res.setHeader(name, value);
      res.setHeader('X-SSR-Cache', 'MISS');
      res.status(response.status).send(body);
      return;
    })
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
