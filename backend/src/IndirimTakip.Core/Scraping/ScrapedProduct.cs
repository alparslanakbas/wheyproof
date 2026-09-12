namespace IndirimTakip.Core.Scraping;

public record ScrapedProduct(
    string Name,
    string Url,
    string? ImageUrl,
    string? Category,
    decimal Price,
    // Serving size (grams) from the real nutrition table. Filled only when the
    // brand provides it reliably; otherwise left empty rather than made up.
    decimal? ServingSizeGrams = null,
    // Old price the store itself declares (Shopify compare_at_price and the
    // like); filled only when the store shows it explicitly. Entirely separate
    // from our "verified discount" (based on price history); shown separately
    // and labeled as the store's own discount.
    decimal? StoreOldPrice = null,
    // The real product description from the brand's own site (plain text).
    // Filled only when the brand provides it reliably; otherwise null rather
    // than made up.
    string? Description = null,
    // Servings per package as directly declared by the brand.
    int? ServingsPerPackage = null,
    // The real nutrition table as normalized JSON, when the regular scrape
    // carries it. Sources that only show it on the product page fill it in the
    // backfill through a separate interface (IProductDetailFetcher).
    string? NutritionJson = null,
    // Protein per serving (grams) parsed from the table above.
    decimal? ProteinPerServingGrams = null,
    // The product's REAL manufacturer brand. Single-brand scrapers never set it
    // (the brand comes from the scraper itself). In a multi-brand source (a
    // retailer catalog) every product carries its own brand: the product should
    // appear under "Optimum Nutrition", not under the retailer, while the store
    // link goes where it is sold.
    string? BrandName = null,
    // Can the product be bought at the store right now?
    //
    // NULL means "we don't know" and must NOT be confused with false. Not every
    // source reports stock; where it doesn't, the field stays empty and no badge
    // is shown. Counting the unknown as "in stock" would amount to made-up data.
    //
    // Out-of-stock products are NOT dropped from the scrape. Dropping them left
    // gaps of days in price history and a broken series once stock returned;
    // the site's claim is an unbroken real price history, and such gaps
    // undermine exactly that claim.
    bool? InStock = null,
    // The store SELLING the product; separate from the brand (manufacturer).
    //
    // NULL = the product is bought from the brand's own store. In a retailer
    // catalog the manufacturer and the seller differ: the product appears under
    // its brand, but the buy link goes to the retailer and the shopper should
    // know who they are buying from.
    //
    // The same product at two sellers is kept as two records; without a barcode
    // (GTIN) there is NO cross-seller matching.
    string? Seller = null);
