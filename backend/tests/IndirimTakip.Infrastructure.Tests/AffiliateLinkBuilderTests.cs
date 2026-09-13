using IndirimTakip.Infrastructure.Deals;

namespace IndirimTakip.Infrastructure.Tests;

public class AffiliateLinkBuilderTests
{
    // Not a real tracking code; a value made up for the test.
    private const string Code = "abc123test";

    private static AffiliateOptions Options() => new()
    {
        TrackingCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hardline"] = Code,
        },
    };

    [Fact]
    public void Tracking_code_is_added_for_a_brand_with_a_program()
    {
        // Exactly the format the brand's own affiliate tool produced:
        // https://www.hardlinenutrition.com/meet-fit-paketi?tracking=...
        var url = AffiliateLinkBuilder.Apply(
            "https://www.hardlinenutrition.com/meet-fit-paketi", "Hardline", Options());

        Assert.Equal($"https://www.hardlinenutrition.com/meet-fit-paketi?tracking={Code}", url);
    }

    [Fact]
    public void Url_is_unchanged_for_a_brand_without_a_program()
    {
        const string original = "https://takehiq.com/products/creatine";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, "HIQ", Options()));
    }

    [Fact]
    public void Brand_name_matches_case_insensitively()
    {
        var url = AffiliateLinkBuilder.Apply("https://x.com/a", "hardline", Options());
        Assert.Contains($"tracking={Code}", url);
    }

    [Fact]
    public void Ampersand_is_used_when_the_url_already_has_a_query()
    {
        // The wrong separator would break the link entirely.
        var url = AffiliateLinkBuilder.Apply(
            "https://www.hardlinenutrition.com/urun?renk=mavi", "Hardline", Options());

        Assert.Equal($"https://www.hardlinenutrition.com/urun?renk=mavi&tracking={Code}", url);
    }

    [Fact]
    public void Parameter_goes_before_a_fragment()
    {
        // Added after the fragment, the server would never see the parameter.
        var url = AffiliateLinkBuilder.Apply(
            "https://www.hardlinenutrition.com/urun#detay", "Hardline", Options());

        Assert.Equal($"https://www.hardlinenutrition.com/urun?tracking={Code}#detay", url);
    }

    [Fact]
    public void Existing_parameter_is_not_added_twice()
    {
        const string original = "https://www.hardlinenutrition.com/urun?tracking=someoneelse";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, "Hardline", Options()));
    }

    [Fact]
    public void Blank_code_leaves_the_url_alone()
    {
        var options = new AffiliateOptions
        {
            TrackingCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Hardline"] = "  " },
        };
        const string original = "https://www.hardlinenutrition.com/urun";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, "Hardline", options));
    }

    [Fact]
    public void Url_is_unchanged_without_a_brand_name()
    {
        const string original = "https://www.hardlinenutrition.com/urun";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, null, Options()));
    }
}
