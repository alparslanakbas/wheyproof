namespace IndirimTakip.Core.Entities;

// Guide articles (e.g. "Creatine: what to know"): informational pieces for SEO
// and trust, independent of product data. Managed by hand like coupons: never
// generated, added by hand (embedded in the repo or through an admin-key
// protected endpoint).
public class Article
{
    public int Id { get; set; }

    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string Summary { get; set; }
    public required string Body { get; set; }
    public string? CoverImageUrl { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public bool IsPublished { get; set; } = true;
}
