using IndirimTakip.Infrastructure.Articles;
using Xunit;

namespace IndirimTakip.Infrastructure.Tests;

public class ArticleContentTests
{
    // The frontend links to these slugs from category guides, calculators and
    // the guides page's learning paths. A missing one means a link that 404s,
    // and nothing else would catch it.
    private static readonly string[] LinkedSlugs =
    [
        "how-to-choose-whey-protein",
        "creatine-what-to-know",
        "how-to-choose-a-pre-workout",
        "bcaa-vs-eaa",
        "electrolytes-explained",
        "how-to-use-a-mass-gainer",
        "do-fat-burners-work",
        "how-to-choose-protein-bars",
        "how-to-choose-vitamins-and-minerals",
    ];

    [Fact]
    public void Every_slug_the_frontend_links_to_is_embedded()
    {
        var slugs = ArticleSeeder.LoadEmbedded().Select(a => a.Slug).ToHashSet();

        foreach (var slug in LinkedSlugs)
            Assert.Contains(slug, slugs);
    }

    [Fact]
    public void Every_embedded_article_fits_the_database_columns()
    {
        var articles = ArticleSeeder.LoadEmbedded();

        Assert.NotEmpty(articles);
        foreach (var article in articles)
        {
            // Title/Slug max 200, Summary max 500 (AppDbContext). A longer value
            // would fail SaveChanges at startup, taking the whole seed down.
            Assert.InRange(article.Title.Length, 1, 200);
            Assert.InRange(article.Slug.Length, 1, 200);
            Assert.InRange(article.Summary.Length, 1, 500);
            Assert.StartsWith("<p>", article.Body);
        }
    }

    [Fact]
    public void Embedded_articles_link_only_to_english_routes()
    {
        // The articles were adapted from the Turkish site; a leftover Turkish
        // route (/kategori, /hesaplama) would be a dead link on this site.
        foreach (var article in ArticleSeeder.LoadEmbedded())
        {
            Assert.DoesNotContain("/kategori/", article.Body);
            Assert.DoesNotContain("/hesaplama/", article.Body);
        }
    }

    [Fact]
    public void Parse_reads_the_header_and_body()
    {
        var article = ArticleSeeder.Parse("sample", """
            <!--
            title: Sample Title
            summary: A short summary: with a colon inside.
            published: 2026-09-12T09:00:00Z
            -->
            <p>Body text.</p>
            """);

        Assert.Equal("Sample Title", article.Title);
        Assert.Equal("A short summary: with a colon inside.", article.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), article.PublishedAt);
        Assert.Equal("<p>Body text.</p>", article.Body);
    }

    [Fact]
    public void Parse_rejects_a_file_without_a_header()
    {
        Assert.Throws<FormatException>(() => ArticleSeeder.Parse("broken", "<p>No header here.</p>"));
    }

    [Fact]
    public void Parse_rejects_a_header_without_a_summary()
    {
        Assert.Throws<FormatException>(() => ArticleSeeder.Parse("broken", """
            <!--
            title: Only a title
            published: 2026-09-12T09:00:00Z
            -->
            <p>Body.</p>
            """));
    }
}
