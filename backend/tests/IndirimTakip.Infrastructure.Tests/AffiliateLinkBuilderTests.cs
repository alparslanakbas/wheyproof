using IndirimTakip.Infrastructure.Deals;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Tests;

// Codes and ids here are made up for the tests, not real accounts.
public class AffiliateLinkBuilderTests
{
    private static AffiliateOptions Options(params (string Host, string Rule)[] rules)
    {
        var options = new AffiliateOptions();
        foreach (var (host, rule) in rules)
            options.Links[host] = rule;
        return options;
    }

    // UpPromote's default link: the store URL plus sca_ref.
    [Fact]
    public void Query_pair_is_appended_for_the_store()
    {
        var url = AffiliateLinkBuilder.Apply(
            "https://www.transparentlabs.com/products/bulk-preworkout",
            Options(("transparentlabs.com", "sca_ref=12345.abcde")));

        Assert.Equal("https://www.transparentlabs.com/products/bulk-preworkout?sca_ref=12345.abcde", url);
    }

    // Awin's deep link wraps the product URL, encoded, in its own address.
    [Fact]
    public void Redirect_template_wraps_the_encoded_product_url()
    {
        var url = AffiliateLinkBuilder.Apply(
            "https://www.bulksupplements.com/products/creatine?variant=42",
            Options(("bulksupplements.com", "https://www.awin1.com/cread.php?awinmid=111&awinaffid=222&ued={url}")));

        Assert.Equal(
            "https://www.awin1.com/cread.php?awinmid=111&awinaffid=222&ued=" +
            "https%3A%2F%2Fwww.bulksupplements.com%2Fproducts%2Fcreatine%3Fvariant%3D42",
            url);
    }

    // The bug this design fixes: Bodybuilding.com sells Optimum Nutrition under
    // Optimum's brand name. Its links must carry Bodybuilding.com's code, the
    // store that pays, and Optimum's own code must stay on Optimum's site.
    [Fact]
    public void Rule_follows_the_store_host_not_the_brand()
    {
        var options = Options(
            ("optimumnutrition.com", "rfsn=optimum.code"),
            ("bodybuilding.com", "https://bodybuilding.sjv.io/c/1/2/3?u={url}"));

        var retailer = AffiliateLinkBuilder.Apply("https://www.bodybuilding.com/products/on-gold-standard", options);
        var brandSite = AffiliateLinkBuilder.Apply("https://www.optimumnutrition.com/en-us/products/gold-standard", options);

        Assert.StartsWith("https://bodybuilding.sjv.io/c/1/2/3?u=", retailer);
        Assert.DoesNotContain("optimum.code", retailer);
        Assert.EndsWith("?rfsn=optimum.code", brandSite);
    }

    [Theory]
    [InlineData("https://www.kaged.com/products/a")]
    [InlineData("https://kaged.com/products/a")]
    [InlineData("https://WWW.KAGED.COM/products/a")]
    public void Host_matches_with_or_without_www_and_in_any_case(string productUrl)
    {
        var url = AffiliateLinkBuilder.Apply(productUrl, Options(("www.Kaged.com", "ref=k1")));
        Assert.EndsWith("ref=k1", url);
    }

    [Fact]
    public void Url_is_unchanged_for_a_store_without_a_rule()
    {
        const string original = "https://www.questnutrition.com/products/bar";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, Options(("kaged.com", "ref=k1"))));
    }

    [Fact]
    public void Ampersand_is_used_when_the_url_already_has_a_query()
    {
        // Size links carry ?variant=; the wrong separator would break them.
        var url = AffiliateLinkBuilder.Apply(
            "https://nutricost.com/products/whey?variant=7", Options(("nutricost.com", "rfsn=1.a")));

        Assert.Equal("https://nutricost.com/products/whey?variant=7&rfsn=1.a", url);
    }

    [Fact]
    public void Parameter_goes_before_a_fragment()
    {
        var url = AffiliateLinkBuilder.Apply("https://nutricost.com/products/whey#reviews", Options(("nutricost.com", "rfsn=1.a")));
        Assert.Equal("https://nutricost.com/products/whey?rfsn=1.a#reviews", url);
    }

    [Fact]
    public void Existing_parameter_is_not_added_twice()
    {
        const string original = "https://nutricost.com/products/whey?rfsn=someoneelse";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, Options(("nutricost.com", "rfsn=1.a"))));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("no-equals-sign")]
    [InlineData("=value-without-name")]
    [InlineData("name-without-value=")]
    [InlineData("http://insecure.example/c?u={url}")]
    public void Malformed_rule_leaves_the_url_alone(string rule)
    {
        const string original = "https://nutricost.com/products/whey";
        Assert.Equal(original, AffiliateLinkBuilder.Apply(original, Options(("nutricost.com", rule))));
    }

    // The rules reach the app as environment variables from .env through
    // docker-compose. The key carries a dotted host and an Awin template
    // carries & and =; if either broke binding, links would silently stay
    // untracked. This goes through the same provider and binder the app uses.
    [Fact]
    public void Rules_bind_from_environment_variables_with_a_dotted_host()
    {
        const string prefix = "WPTEST_AFFILIATE_BINDING_";
        const string awin = "https://www.awin1.com/cread.php?awinmid=111&awinaffid=222&ued={url}";
        Environment.SetEnvironmentVariable($"{prefix}Affiliate__Links__bulksupplements.com", awin);
        Environment.SetEnvironmentVariable($"{prefix}Affiliate__Links__transparentlabs.com", "sca_ref=12345.abcde");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix)
                .Build();
            var options = new AffiliateOptions();
            configuration.GetSection("Affiliate").Bind(options);

            Assert.Equal(awin, options.Links["bulksupplements.com"]);
            Assert.StartsWith(
                "https://www.awin1.com/cread.php?awinmid=111&awinaffid=222&ued=https%3A%2F%2F",
                AffiliateLinkBuilder.Apply("https://www.bulksupplements.com/products/creatine", options));
            Assert.EndsWith(
                "?sca_ref=12345.abcde",
                AffiliateLinkBuilder.Apply("https://www.transparentlabs.com/products/bulk", options));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}Affiliate__Links__bulksupplements.com", null);
            Environment.SetEnvironmentVariable($"{prefix}Affiliate__Links__transparentlabs.com", null);
        }
    }

    [Fact]
    public void Relative_or_empty_url_is_left_alone()
    {
        var options = Options(("nutricost.com", "rfsn=1.a"));
        Assert.Equal("/products/whey", AffiliateLinkBuilder.Apply("/products/whey", options));
        Assert.Equal("", AffiliateLinkBuilder.Apply("", options));
    }
}
