namespace IndirimTakip.Infrastructure.Articles;

// The list view doesn't need Body (long HTML); only the detail page does.
public record ArticleSummaryDto(int Id, string Title, string Slug, string Summary, string? CoverImageUrl, DateTimeOffset PublishedAt);

public record ArticleDto(int Id, string Title, string Slug, string Summary, string Body, string? CoverImageUrl, DateTimeOffset PublishedAt);
