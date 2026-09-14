namespace IndirimTakip.Core.Scraping;

// Information taken from the product detail page that the regular scrape
// (ScrapeAsync) doesn't return. Description and nutrition sit on the SAME page,
// so they come back together in one call; separate interfaces would download
// every product page twice and load the stores for nothing.
// Fields that aren't found stay null; no guessed or made-up values.
// ServingSizeGrams / ServingsPerPackage DEFAULT to null, so fetchers that don't
// provide them keep the signature. They are filled only when the source DECLARES
// them directly (e.g. "Serving Size: 32g" and "Servings: 68" on the page).
// Nothing is inferred from the description text: that job belongs to
// ProductAttributeParser.ExtractServingSizeGrams, and a derived value must not
// overwrite the source's own statement.
public record ProductDetails(
    string? Description,
    string? NutritionJson,
    decimal? ProteinPerServingGrams,
    decimal? ServingSizeGrams = null,
    int? ServingsPerPackage = null);

// This information exists only on the product DETAIL page, so scrapers that
// implement this interface send a separate request per product. Stores whose
// regular scrape already carries description and nutrition (e.g. Shopify's
// body_html) don't need it. Called only by ProductDetailBackfillService, on its
// own schedule and only for products missing the data; it doesn't affect the
// regular price scrape.
public interface IProductDetailFetcher
{
    Task<ProductDetails> FetchDetailsAsync(string productUrl, CancellationToken cancellationToken = default);

    // Whether THIS instance has anything to fetch. One Shopify scraper class
    // serves every store, and only some stores print details on the product
    // page; without this, the backfill would download every store's pages to
    // find nothing.
    bool HasProductDetails => true;
}
