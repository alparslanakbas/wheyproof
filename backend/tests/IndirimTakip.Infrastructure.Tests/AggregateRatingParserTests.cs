using IndirimTakip.Infrastructure.Scraping;

namespace IndirimTakip.Infrastructure.Tests;

// Samples were taken from real product pages. The stores run on different
// platforms but all use the same schema.org fields; these tests keep that
// assumption from breaking silently.
public class AggregateRatingParserTests
{
    [Fact]
    public void Reads_the_rating_from_a_shopify_page()
    {
        const string html = """
            <script type="application/ld+json">
            {"@type":"Product","name":"HIQ Beta Alanine 300g Unflavored",
             "aggregateRating":{"@type":"AggregateRating","ratingValue":"4.77","reviewCount":31},
             "review":[{"@type":"Review","reviewRating":{"@type":"Rating","ratingValue":5,"bestRating":5}}]}
            </script>
            """;

        var (value, count) = AggregateRatingParser.Parse(html);

        Assert.Equal(4.77m, value);
        Assert.Equal(31, count);
    }

    [Fact]
    public void Reads_a_numeric_rating_value()
    {
        const string html = """{"aggregateRating":{"@type":"AggregateRating","ratingValue":4.77,"reviewCount":30}}""";

        var (value, count) = AggregateRatingParser.Parse(html);

        Assert.Equal(4.77m, value);
        Assert.Equal(30, count);
    }

    [Fact]
    public void Does_not_mistake_a_single_review_for_the_average()
    {
        // A single review's own block comes BEFORE aggregateRating on the page. That
        // value isn't an average and must not be taken.
        const string html = """
            {"review":{"@type":"Review","reviewRating":{"ratingValue":5,"bestRating":5}},
             "aggregateRating":{"@type":"AggregateRating","ratingValue":"4.12","reviewCount":9}}
            """;

        var (value, count) = AggregateRatingParser.Parse(html);

        Assert.Equal(4.12m, value);
        Assert.Equal(9, count);
    }

    [Fact]
    public void Returns_null_without_a_rating_block()
    {
        var (value, count) = AggregateRatingParser.Parse("""<div class="rating no-rating">0 reviews</div>""");

        Assert.Null(value);
        Assert.Null(count);
    }

    [Theory]
    [InlineData("""{"aggregateRating":{"ratingValue":9.4,"reviewCount":12}}""")]   // not a 5-point scale
    [InlineData("""{"aggregateRating":{"ratingValue":4.5,"reviewCount":0}}""")]    // nobody rated
    public void Rejects_invalid_values(string html)
    {
        var (value, count) = AggregateRatingParser.Parse(html);

        Assert.Null(value);
        Assert.Null(count);
    }

    [Fact]
    public void Reads_the_decimal_separator_independently_of_culture()
    {
        // Even on a machine with a comma decimal locale "4.88" must not be read as
        // 488: JSON always uses a dot.
        var (value, _) = AggregateRatingParser.Parse("""{"aggregateRating":{"ratingValue":4.88,"reviewCount":278}}""");

        Assert.Equal(4.88m, value);
    }
}
