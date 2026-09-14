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
        var html = DigestService.BuildDigestHtml("", "https://api.example.com/u/t", "https://www.wheyproof.com", "PO Box 100 <Suite 5>");

        Assert.Contains("PO Box 100 &lt;Suite 5&gt;", html);
        Assert.Contains("Unsubscribe", html);
    }
}
