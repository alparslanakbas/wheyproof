using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Tests;

// The footer's postal address is optional since 2026-10-05: the owner chose to
// send like the Turkish site, without one (see DigestService). When it is set it
// is printed, encoded; when it is not, nothing stands in for it.
public class DigestPostalAddressTests
{
    private static IConfiguration Config(string? address) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Newsletter:PostalAddress"] = address })
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_means_no_address(string? address) =>
        Assert.Null(DigestService.PostalAddressFrom(Config(address)));

    [Fact]
    public void A_multi_line_address_becomes_one_line() =>
        Assert.Equal(
            "WheyProof, PO Box 100, Austin, TX 78701",
            DigestService.PostalAddressFrom(Config("WheyProof\n PO Box 100 \r\nAustin, TX 78701")));

    [Fact]
    public void Without_an_address_the_footer_names_only_the_sender()
    {
        var html = DigestService.BuildDigestHtml("", 4, "https://api.example.com/u/t", "https://www.wheyproof.com", null);

        Assert.Contains("WheyProof<br>", html);
        Assert.DoesNotContain("&middot;", html);
        Assert.Contains("Unsubscribe", html);
    }

    [Fact]
    public void The_address_appears_in_the_footer_encoded()
    {
        var html = DigestService.BuildDigestHtml("", 4, "https://api.example.com/u/t", "https://www.wheyproof.com", "PO Box 100 <Suite 5>");

        Assert.Contains("WheyProof &middot; PO Box 100 &lt;Suite 5&gt;", html);
        Assert.Contains("Unsubscribe", html);
    }

    // The first preview showed 4 cards under a fixed "6 real price drops".
    [Theory]
    [InlineData(4, "4 real price drops")]
    [InlineData(1, "1 real price drop ")]
    public void The_headline_count_is_the_number_of_deals_shown(int count, string expected)
    {
        var html = DigestService.BuildDigestHtml("", count, "https://api.example.com/u/t", "https://www.wheyproof.com", null);

        Assert.Contains(expected, html);
        Assert.DoesNotContain("6 real price drops", html);
    }
}
