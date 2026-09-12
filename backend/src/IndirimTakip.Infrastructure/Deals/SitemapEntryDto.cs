namespace IndirimTakip.Infrastructure.Deals;

// HasReviewContent: a product review page enters the sitemap only when it has
// at least one real content source (the brand's description or a nutrition
// table); otherwise thin pages would be offered to Google for crawling. The page
// itself stays available for every product (if linked); it just isn't in the
// sitemap.
public record SitemapEntryDto(int Id, string Name, DateTimeOffset LastModifiedAt, bool HasReviewContent);
