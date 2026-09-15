# WheyProof

🔗 **Live site:** [wheyproof.com](https://www.wheyproof.com)

## What it does

Supplement stores show "sale" badges all the time, but the crossed-out
"was" price is the store's own claim, not something anyone checked.
WheyProof records the real price of US protein and sports-nutrition products
several times a day and uses that history to show whether a discount is an
actual price drop or just a label.

In short: it trusts the price history it collected, not what the store says.

## Features

- **Price history from the stores themselves** — every scrape leaves a
  `PriceHistory` row; discounts, 30-day lows and the reference price are
  computed from that history.
- **Store claims kept separate** — the store's own "was" price is shown as the
  store's claim, not as a verified discount.
- **Nutrition and value metrics** — serving size, protein per serving, cost per
  serving and cost per 30 g of protein, only when the store publishes the data.
  Nothing is estimated: a value that can't be calculated stays empty.
- **Nutrition labels read from product images** — when a store only publishes
  its Nutrition Facts as an image, the label is read with Tesseract OCR and
  accepted only if the calories add up from the macros.
- **Stock status** — out-of-stock products stay listed with an "Out of stock"
  badge so their price history has no gaps; shown only for stores that report
  stock.
- **Product and brand comparison** — side-by-side pages for two products
  (`/compare-products/:pair`) or two brands (`/compare/:pair`).
- **Calculators** — protein needs, daily calories (TDEE), BMI, water intake and
  supplement dosage/cost for creatine, beta-alanine, citrulline, betaine and
  EAAs (`/calculators/*`).
- **Guides and glossary** — articles (`/guides`) and a terms glossary
  (`/glossary`); plus brand × category pages (`/brand/:brand/:category`).
- **Server-side search, filters, sorting and pagination** — brand, category,
  seller, price range and free-text search.
- **Product pages and reviews** — price chart with selectable ranges
  (`/product/:id/:slug`) and data-based review pages (`/review/:id/:slug`).
- **Watchlist and price alerts** — no account needed; follow products with an
  email address and get notified when the price drops.
- **Newsletter** — weekly deals digest with double opt-in.
- **Affiliate links by store** — "Go to store" links carry tracking only for
  stores with a partner program; the rule is chosen by the store's host and
  configured on the server, never in the repo.
- **SSR + SEO** — Angular SSR with hydration, per-page title/meta/Open Graph,
  schema.org `Product`/`Offer`/`FAQPage` data, dynamic `sitemap.xml` and
  `robots.txt`, IndexNow pings.
- **PWA** — installable, with a service worker.
- **Light / dark / system theme.**

## Sources

24 stores, all scraped from their public Shopify storefronts:
BulkSupplements, Naked Nutrition, Transparent Labs, Momentous, RAW Nutrition,
Gorilla Mind, Nutricost, Kaged, MuscleTech, Promix, Quest Nutrition,
Clean Simple Eats, Jocko Fuel, Optimum Nutrition, Orgain, Ascent, Ghost,
Bodybuilding.com (retailer), MusclePharm, Animal, Bare Performance Nutrition,
Bounce Nutrition, Ultimate Paleo Protein and CON-CRET.

Stores are listed in
`backend/src/IndirimTakip.Infrastructure/Scraping/Shopify/ShopifyStores.cs`;
a new Shopify store is one line there.

## Tech stack

**Backend**
- .NET 10, ASP.NET Core minimal API
- PostgreSQL 18 + EF Core (Npgsql)
- `HttpClient`-based scraping with HtmlAgilityPack; no browser automation
- Tesseract OCR for nutrition label images
- SixLabors.ImageSharp — product images are resized and served from our own
  server instead of hotlinking store CDNs
- [Brevo](https://brevo.com) — transactional email (confirmations, price
  alerts, digest)

**Frontend**
- Angular 22 (standalone components, Signals)
- Angular SSR + hydration, Express 5
- Tailwind CSS v4
- PWA (manifest + service worker)

**Infrastructure**
- [Oracle Cloud](https://www.oracle.com/cloud/) VM (Ampere ARM), Docker Compose
- [Caddy](https://caddyserver.com) reverse proxy with automatic HTTPS
- PostgreSQL in the same Compose project, reachable only from the Compose
  network
- [Cloudflare](https://cloudflare.com) — DNS, CDN, edge security; the admin
  panel is behind Cloudflare Access
- GitHub Actions — every push to the main branch deploys and then checks the
  services are really up

**Tests**
- Backend: xUnit
- Frontend: Vitest

## Project structure

```
backend/
  src/
    IndirimTakip.Api/                  ASP.NET Core API (endpoints)
    IndirimTakip.Core/                 Entities, scraper interfaces
    IndirimTakip.Infrastructure/       EF Core, scrapers, background jobs, services
    IndirimTakip.Infrastructure.Tests/ xUnit tests
frontend/
  src/
    app/
      core/                            Services, models, calculators, content
      deals-list/                      Home page list, filters, pagination
      product-modal/                   Product detail + price chart
      product-review-page/             Data-based review pages
caddy/                                 Site config picked up by the shared Caddy
docker-compose.yml                     Database, backend and frontend services
```

The C# namespaces still start with `IndirimTakip`: the code base was forked
from a Turkish sister project and the names were kept to avoid a large,
purely cosmetic rename.
