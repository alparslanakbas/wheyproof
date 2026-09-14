using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// CAN-SPAM requires a physical postal address in every commercial email, so the
// digest must not go out without one, and nothing may stand in for it.
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
    public async Task Without_an_address_nothing_is_sent(string? address)
    {
        // The database, deals and sender are never touched: the guard runs first.
        var service = new DigestService(null!, null!, null!, Config(address), NullLogger<DigestService>.Instance);

        var result = await service.SendDigestAsync("https://api.example.com");

        Assert.Equal(0, result.SubscriberCount);
        Assert.NotNull(result.SkippedReason);
    }

    [Fact]
    public void A_multi_line_address_becomes_one_line() =>
        Assert.Equal(
            "WheyProof, PO Box 100, Austin, TX 78701",
            DigestService.PostalAddressFrom(Config("WheyProof\n PO Box 100 \r\nAustin, TX 78701")));

    [Fact]
    public void The_address_appears_in_the_footer_encoded()
    {
        var html = DigestService.BuildDigestHtml("", 4, "https://api.example.com/u/t", "https://www.wheyproof.com", "PO Box 100 <Suite 5>");

        Assert.Contains("PO Box 100 &lt;Suite 5&gt;", html);
        Assert.Contains("Unsubscribe", html);
    }

    // The first preview showed 4 cards under a fixed "6 real price drops".
    [Theory]
    [InlineData(4, "4 real price drops")]
    [InlineData(1, "1 real price drop ")]
    public void The_headline_count_is_the_number_of_deals_shown(int count, string expected)
    {
        var html = DigestService.BuildDigestHtml("", count, "https://api.example.com/u/t", "https://www.wheyproof.com", "PO Box 100");

        Assert.Contains(expected, html);
        Assert.DoesNotContain("6 real price drops", html);
    }

    // The preview must not look ready to send while sending is blocked.
    [Fact]
    public void Preview_without_an_address_says_sending_is_blocked() =>
        Assert.Contains("NOT SET", DigestService.PreviewPostalAddress(Config(null)));

    [Fact]
    public void Preview_uses_the_real_address_once_set() =>
        Assert.Equal("PO Box 100, Austin, TX 78701", DigestService.PreviewPostalAddress(Config("PO Box 100\nAustin, TX 78701")));
}
