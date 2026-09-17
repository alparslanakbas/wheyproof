using System.Collections.Concurrent;
using IndirimTakip.Core.Entities;
using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Scraping;

public class ScrapeIngestionService(
    AppDbContext db,
    ProductWatchNotifier watchNotifier,
    IndexNowClient indexNow,
    IConfiguration configuration)
{
    /// <summary>
    /// Per-source concurrency lock. Two scrapes of the same source must not run at
    /// once: on the Turkish site a manually started 15-minute scrape was still
    /// running when the daily service kicked in, the other server got twice the
    /// requests and BOTH hit its rate limit.
    ///
    /// The lock is in-process: enough while a single container runs. Running several
    /// instances would need a database-backed lock.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScrapeLocks = new();

    public async Task<int> IngestAsync(IBrandScraper scraper, CancellationToken cancellationToken = default)
    {
        var gate = ScrapeLocks.GetOrAdd(scraper.BrandName, _ => new SemaphoreSlim(1, 1));

        // Rejected outright rather than awaited: queueing would mean the second
        // scrape starting over right after the first finishes, pulling data that is
        // already fresh.
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException($"A scrape of {scraper.BrandName} is already running; this trigger was skipped.");

        try
        {
            var scrapedProducts = await scraper.ScrapeAsync(cancellationToken);
            return await IngestCoreAsync(scraper, scrapedProducts, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Takes products collected SOMEWHERE ELSE and runs them through the same
    /// ingestion path.
    ///
    /// WHY IT EXISTS: some sources block the server's datacenter address (e.g. with
    /// a Cloudflare managed challenge) while a residential connection gets a normal
    /// 200. Such a source can be scraped elsewhere and the result sent here. Where
    /// the data is collected changes; the ingestion logic does NOT: brand
    /// resolution, category inference, price history and staleness are all the same
    /// code.
    ///
    /// The lock is shared: while an in-server scrape of the same source runs, an
    /// external submission is rejected too.
    /// </summary>
    public async Task<int> IngestAsync(
        IBrandScraper scraper,
        IReadOnlyList<ScrapedProduct> scrapedProducts,
        CancellationToken cancellationToken = default)
    {
        var gate = ScrapeLocks.GetOrAdd(scraper.BrandName, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException($"A scrape of {scraper.BrandName} is already running; this submission was skipped.");

        try
        {
            return await IngestCoreAsync(scraper, scrapedProducts, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> IngestCoreAsync(
        IBrandScraper scraper,
        IReadOnlyList<ScrapedProduct> scrapedProducts,
        CancellationToken cancellationToken)
    {
        var scrapedAt = DateTimeOffset.UtcNow;

        // Brands are cached by name: a multi-brand source (a retailer catalog) needs
        // a brand resolved per product, and a query per product would mean hundreds
        // of round trips.
        // Several brand rows can share a name (created when two scrapes run at once
        // and both decide "brand missing, create it"), so it isn't converted with
        // ToDictionary: a repeated name would throw ArgumentException and take the
        // whole scrape down. The first row wins; duplicates are already deduplicated
        // in the lists.
        var brandsByName = new Dictionary<string, Brand>();
        // Second index: the name with Turkish letters folded, case-insensitive. It
        // exists because of a measured bug: retailers spell the same manufacturer
        // differently and the dictionary above is ORDINAL, so "TREC" and "Trec" were
        // treated as different and a SECOND brand row was created. .NET's
        // culture-independent comparison also doesn't fold the dotted İ at all, so
        // "PRİME NUTRİTİON" didn't match "Prime Nutrition" either.
        //
        // This doesn't replace BrandNameNormalizer, which is for REALLY different
        // spellings ("Proteinocean → ProteinOcean"). This index only closes letter
        // and accent differences and keeps the alias list from growing by dozens of
        // lines per retailer.
        //
        // Measured on the Turkish catalog (96 brands): not a single folding collision,
        // so this index merges no existing brands.
        var brandsByFoldedName = new Dictionary<string, Brand>();
        foreach (var existingBrand in await db.Brands.ToListAsync(cancellationToken))
        {
            brandsByName.TryAdd(existingBrand.Name, existingBrand);
            brandsByFoldedName.TryAdd(FoldBrandName(existingBrand.Name), existingBrand);
        }

        Brand ResolveBrand(string rawName)
        {
            // The alias dictionary is applied HERE, not in the scrapers. Scrapers
            // had to call it one by one and that could be skipped silently: one
            // scraper was written BEFORE BrandNameNormalizer, never called it, and
            // NONE of the aliases worked for that source (one alias created a second
            // brand row with 65 products). A central call guarantees it for every
            // source; the scrapers' own calls are harmless because normalization is
            // idempotent.
            var name = BrandNameNormalizer.Normalize(rawName);

            if (brandsByName.TryGetValue(name, out var existing))
                return existing;

            var folded = FoldBrandName(name);
            if (brandsByFoldedName.TryGetValue(folded, out var sameBrandDifferentCase))
            {
                // The name is NOT changed; the product is only linked to the existing
                // row: renaming a brand breaks its slug and brand page URL.
                brandsByName[name] = sameBrandDifferentCase;
                return sameBrandDifferentCase;
            }

            var created = new Brand { Name = name, BaseUrl = scraper.BaseUrl, IsActive = true };
            db.Brands.Add(created);
            brandsByName[name] = created;
            brandsByFoldedName[folded] = created;
            return created;
        }

        // Existing products are loaded by URL, not by BRAND: a brand filter would miss
        // most products in a multi-brand scrape. The loop only looks at scraped URLs
        // anyway, so single-brand scrapers behave the same.
        var scrapedUrls = scrapedProducts.Select(sp => sp.Url).Distinct().ToList();
        // Which URLs map to more than one product (variant) in this scrape; needed
        // below to choose the matching strategy.
        var scrapedCountByUrl = scrapedProducts
            .GroupBy(sp => sp.Url)
            .ToDictionary(g => g.Key, g => g.Count());

        // The last recorded price, used to decide "did the content really change"
        // for <lastmod>. The correlated subquery + FirstOrDefault pattern is the
        // proven path EF Core translates reliably in this codebase (see the DealRow
        // note in DealsQueryService).
        // IgnoreQueryFilters is REQUIRED: if a hidden product isn't visible here, it
        // is taken for "new" below and a DUPLICATE is created. Duplicate rows are a
        // failure this codebase has hit again and again.
        var lastPrices = await db.Products
            .IgnoreQueryFilters()
            .Where(p => scrapedUrls.Contains(p.Url))
            .Select(p => new
            {
                p.Id,
                LastPrice = (decimal?)p.PriceHistories
                    .OrderByDescending(ph => ph.ScrapedAt)
                    .Select(ph => ph.Price)
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(x => x.Id, x => x.LastPrice, cancellationToken);
        // The URL is NOT UNIQUE: on one Turkish store's API every flavor was a
        // separate product record (with its own stock and price) sharing the same page
        // slug. ToDictionary(p => p.Url) was used here, and from the second scrape on
        // "An item with the same key has already been added" cancelled the brand's
        // WHOLE scrape (it produced no data for two days).
        //
        // A list per URL is kept instead of a dictionary. URLs with a single record
        // behave EXACTLY as before (a rename is updated in place); only when a URL has
        // several records are they told apart by name. The distinction matters:
        // always matching by name would orphan the old row and create a new one
        // whenever a store renamed a product.
        var existingByUrl = (await db.Products
                // Same reason: a hidden product not visible here gets duplicated.
                .IgnoreQueryFilters()
                .Where(p => scrapedUrls.Contains(p.Url))
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.Url)
            .ToDictionary(g => g.Key, g => g.ToList());

        var touchedProducts = new List<Product>();
        // Only products seen for the FIRST TIME in this scrape, to report new URLs to
        // IndexNow. The protocol requires not resubmitting unchanged URLs; a price
        // change doesn't change the URL, so only new products are collected here.
        var newProducts = new List<Product>();

        foreach (var scraped in scrapedProducts)
        {
            // If the store gives no category, infer it from the name; search working
            // independently of the brand depends on this.
            // The brand name is REMOVED before category inference: retailer sources
            // write the brand into the product name, and a brand name containing
            // "protein" made every product look like protein powder.
            var brandForCategory = scraped.BrandName ?? scraper.BrandName;
            var category = scraped.Category ?? ProductAttributeParser.InferCategory(scraped.Name, brandForCategory);
            var size = ProductAttributeParser.ExtractSize(scraped.Name);
            var flavor = ProductAttributeParser.ExtractFlavor(scraped.Name);

            Product? product = null;
            if (existingByUrl.TryGetValue(scraped.Url, out var sameUrlProducts))
            {
                // Is the URL really unique for this brand? If it has a single match
                // both in the database and in this scrape, the old behavior is kept:
                // match by URL, so a renamed product is updated in place without
                // orphaning the row.
                //
                // If EITHER side is plural, matching by name is REQUIRED. Looking only
                // at the database side wouldn't do: when a URL first becomes plural (1
                // row in the DB, 2 variants in the scrape), both variants would write
                // to the same row and one would silently overwrite the other.
                var isUnique = sameUrlProducts.Count == 1
                    && scrapedCountByUrl.GetValueOrDefault(scraped.Url) == 1;

                product = isUnique
                    ? sameUrlProducts[0]
                    : sameUrlProducts.Find(p => p.Name == scraped.Name);
            }

            if (product is null)
            {
                product = new Product
                {
                    Brand = ResolveBrand(scraped.BrandName ?? scraper.BrandName),
                    Name = scraped.Name,
                    Url = scraped.Url,
                    ImageUrl = scraped.ImageUrl,
                    Category = category,
                    Size = size,
                    Flavor = flavor,
                    InStock = scraped.InStock,
                    Seller = scraped.Seller,
                    // Serving size: the scraper's structured value first (a real
                    // nutrition table, the most reliable source), otherwise inferred
                    // from the store's description text.
                    ServingSizeGrams = scraped.ServingSizeGrams
                        ?? ProductAttributeParser.ExtractServingSizeGrams(scraped.Description),
                    ServingsPerPackage = scraped.ServingsPerPackage,
                    Description = scraped.Description,
                    NutritionJson = scraped.NutritionJson,
                    ProteinPerServingGrams = scraped.ProteinPerServingGrams,
                    NutritionLabelImageUrl = scraped.NutritionLabelImageUrl,
                };
                product.ContentUpdatedAt = DateTimeOffset.UtcNow;
                db.Products.Add(product);
                newProducts.Add(product);

                // The new record joins the lookup too, so a second (URL, name) in the
                // same scrape doesn't open a second row. Earlier duplicate rows were
                // created exactly like this: none existed in the database on the
                // first run, so all counted as "new".
                if (existingByUrl.TryGetValue(scraped.Url, out var bucket))
                    bucket.Add(product);
                else
                    existingByUrl[scraped.Url] = [product];
            }
            else
            {
                // Did the content really change? Measuring the same price again does
                // NOT count: the sitemap's <lastmod> depends on it, and marking the
                // whole catalog "changed" on every scrape made Google ignore the
                // signal entirely.
                lastPrices.TryGetValue(product.Id, out var previousPrice);
                var meaningfulChange =
                    previousPrice != scraped.Price
                    || product.Name != scraped.Name
                    // Hand-entered fields stay out of the comparison: a product whose
                    // category was corrected by hand differs from the inferred one on
                    // EVERY scrape and would be marked changed every six hours,
                    // bringing back exactly the lastmod problem described above.
                    || (!product.CategoryIsManual && product.Category != category)
                    || product.Size != size
                    || (scraped.Description is not null && product.Description != scraped.Description)
                    || (!product.NutritionIsManual && scraped.NutritionJson is not null && product.NutritionJson != scraped.NutritionJson)
                    // A stock change is a real content change too: an "Out of stock"
                    // badge appears or disappears on the page. Unlike the old
                    // behavior of marking the whole catalog changed every scrape, it
                    // happens rarely per product, so it doesn't spoil the lastmod
                    // signal.
                    || product.InStock != scraped.InStock
                    || product.Seller != scraped.Seller;

                if (meaningfulChange)
                    product.ContentUpdatedAt = DateTimeOffset.UtcNow;

                product.Name = scraped.Name;
                // The brand is updated too. It used to be set only when the product
                // was FIRST saved, so a fixed spelling at the source left the old
                // record under the old brand: on the Turkish site 67 products fell
                // into a separate brand and the brand was split in two. Normalization
                // (see BrandNameNormalizer) now reaches existing products too, with no
                // manual DB work.
                //
                // Single-brand sources behave the SAME: they send no
                // scraped.BrandName, the value falls back to the scraper's own name
                // and yields the same brand.
                product.Brand = ResolveBrand(scraped.BrandName ?? scraper.BrandName);
                // IF THE SOURCE URL CHANGED, THE LOCAL COPY IS INVALID. Without this
                // line, when a store changed an image the site would keep the old one
                // forever, without an error anywhere.
                if (!string.Equals(product.ImageUrl, scraped.ImageUrl, StringComparison.Ordinal))
                    product.LocalImagePath = null;

                product.ImageUrl = scraped.ImageUrl;
                // A category set by hand in the admin panel survives the crawl.
                if (!product.CategoryIsManual)
                    product.Category = category;
                product.Size = size;
                product.Flavor = flavor;
                product.InStock = scraped.InStock;
                product.Seller = scraped.Seller;
                // Scrapers that don't fetch descriptions send no scraped.Description;
                // the existing value is then NOT reset. Stores whose scrape carries
                // descriptions keep it current on every scrape.
                if (scraped.Description is not null)
                    product.Description = scraped.Description;

                // The serving size is computed AFTER the Description assignment, to use
                // the current description. A structured value from the scraper wins,
                // otherwise it's inferred from the description.
                //
                // NOTHING FOUND KEEPS THE STORED VALUE. The US scrape carries no
                // description, so both sources are null on every crawl; assigning
                // that would erase the serving size read from the label image every
                // six hours, without an error anywhere.
                // A hand-entered panel's serving size was entered with it.
                if (!product.NutritionIsManual)
                {
                    product.ServingSizeGrams = scraped.ServingSizeGrams
                        ?? ProductAttributeParser.ExtractServingSizeGrams(product.Description)
                        ?? product.ServingSizeGrams;
                }

                // Kept when the store stops naming a label image, like the fields
                // above; a new image URL queues the product for a new read.
                if (scraped.NutritionLabelImageUrl is not null)
                    product.NutritionLabelImageUrl = scraped.NutritionLabelImageUrl;

                // Updated only when the store provides it, so stores that don't keep
                // their existing value.
                if (scraped.ServingsPerPackage is not null)
                    product.ServingsPerPackage = scraped.ServingsPerPackage;

                // Nutrition from the regular scrape only where the store's scrape
                // carries it; the backfill service fills the rest (same pattern as
                // Description: a store that doesn't send it keeps the existing value).
                if (scraped.NutritionJson is not null && !product.NutritionIsManual)
                {
                    product.NutritionJson = scraped.NutritionJson;
                    product.ProteinPerServingGrams = scraped.ProteinPerServingGrams;
                }
            }

            product.PriceHistories.Add(new PriceHistory
            {
                Price = scraped.Price,
                StoreOldPrice = scraped.StoreOldPrice,
                ScrapedAt = scrapedAt,
            });
            touchedProducts.Add(product);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Price alerts: new products' ids are only final after SaveChangesAsync, so
        // this runs here.
        await watchNotifier.CheckAndNotifyAsync(touchedProducts.Select(p => p.Id).ToList(), cancellationToken);

        // Report new product URLs to search engines. The product id is only final
        // after saving, so this runs here.
        if (newProducts.Count > 0 && indexNow.IsEnabled)
        {
            var frontendBaseUrl = configuration["FrontendBaseUrl"] ?? "https://www.wheyproof.com";
            var urls = newProducts
                .Select(p => ProductPageUrl(frontendBaseUrl, p.Id, p.Name))
                .ToList();
            await indexNow.SubmitAsync(urls, cancellationToken);
        }

        return scrapedProducts.Count;
    }

    /// <summary>
    /// The canonical product page URL reported to IndexNow.
    /// </summary>
    /// <remarks>
    /// MUST match the frontend route <c>product/:id/:slug</c> (app.routes.ts). This
    /// used to build the Turkish site's <c>/urun/</c> path, so every new product
    /// would have been reported to search engines at a URL that returns 404 here.
    /// </remarks>
    internal static string ProductPageUrl(string frontendBaseUrl, int productId, string productName) =>
        $"{frontendBaseUrl.TrimEnd('/')}/product/{productId}/{Slugifier.Slugify(productName)}";

    /// <summary>
    /// Simplifies a brand name for MATCHING only; the stored name isn't touched.
    ///
    /// Turkish letters are mapped BY HAND, not left to a culture. Both sides are
    /// traps: <c>ToLowerInvariant</c> doesn't lowercase the dotted İ at all
    /// ("VİTAMİN" → "vİtamİn"), while the tr-TR culture breaks English words
    /// ("CREATINE" → "creatıne"). Mapping by hand avoids both.
    ///
    /// Spaces and dots are dropped too: "Dr. Pan" and "Dr Pan", "Big Joy" and
    /// "BigJoy" are the same manufacturer and both had created duplicate brands.
    /// HYPHENS ARE KEPT: in names such as "Z-Konzept" the hyphen is part of the
    /// brand's own spelling, and dropping it would needlessly raise the risk of
    /// merging different manufacturers.
    /// </summary>
    internal static string FoldBrandName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;

        foreach (var ch in name)
        {
            var mapped = ch switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' or 'I' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => ch,
            };

            if (mapped is ' ' or '.' or '\t')
                continue;

            buffer[length++] = char.ToLowerInvariant(mapped);
        }

        return new string(buffer[..length]);
    }
}
