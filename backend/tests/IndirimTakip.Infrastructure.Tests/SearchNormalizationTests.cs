using IndirimTakip.Infrastructure.Deals;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Search text has to be reduced to the SAME form as the database's `lower()`
/// output. Otherwise matching silently returns nothing: no error, just "no
/// results", which is hard to notice.
/// </summary>
public class SearchNormalizationTests
{
    [Theory]
    // REGRESSION: .NET's invariant ToLower() does NOT lowercase the dotted İ at all;
    // a "VİTAMİN" search became "vİtamİn" and never matched "vitamin" in the
    // database. Measured on the Turkish site: "vitamin" returned 260 results while
    // "VİTAMİN" returned 0.
    [InlineData("VİTAMİN", "vitamin")]
    [InlineData("vitamin", "vitamin")]
    [InlineData("VITAMIN", "vitamin")]
    [InlineData("Vitamin", "vitamin")]
    [InlineData("KREATİN", "kreatin")]
    public void Uppercase_spellings_reduce_to_the_same_result(string input, string expected)
    {
        Assert.Equal(expected, DealsQueryService.NormalizeSearchText(input));
    }

    [Theory]
    // The dotless ı folds to i too: Postgres turns "FISTIK" into "fistik" but leaves
    // "Fıstık" as "fıstık". Without folding on both sides, two spellings of the same
    // product didn't find each other (measured: 24 vs 6 results).
    [InlineData("fıstık", "fistik")]
    [InlineData("FISTIK", "fistik")]
    [InlineData("Fıstık", "fistik")]
    public void Dotless_and_dotted_i_reduce_to_the_same_letter(string input, string expected)
    {
        Assert.Equal(expected, DealsQueryService.NormalizeSearchText(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_returns_empty(string? input)
    {
        Assert.Equal(string.Empty, DealsQueryService.NormalizeSearchText(input));
    }

    [Fact]
    public void Leading_and_trailing_spaces_are_trimmed()
    {
        Assert.Equal("torq protein", DealsQueryService.NormalizeSearchText("  Torq Protein  "));
    }

    [Fact]
    public void Spaces_between_words_are_KEPT()
    {
        // Word-based search depends on these spaces; collapsing them would turn
        // "Torq Protein" into one word and bring the bug back.
        Assert.Contains(' ', DealsQueryService.NormalizeSearchText("Torq Protein"));
    }
}
