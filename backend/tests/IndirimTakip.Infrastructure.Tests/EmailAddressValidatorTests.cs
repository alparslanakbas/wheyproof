using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndirimTakip.Infrastructure.Tests;

// These tests go out to DNS: there is no other way to verify that a domain
// really resolves.
public class EmailAddressValidatorTests
{
    private static EmailAddressValidator Create() =>
        new(NullLogger<EmailAddressValidator>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain-text")]
    [InlineData("address with spaces@gmail.com")]
    [InlineData("user@nodomain")]
    public async Task Malformed_address_is_rejected(string email)
        => Assert.False(await Create().IsDeliverableAsync(email));

    [Fact]
    public async Task Disposable_provider_is_rejected()
        => Assert.False(await Create().IsDeliverableAsync("someone@mailinator.com"));

    [Fact]
    public async Task Nonexistent_domain_is_rejected()
        => Assert.False(await Create().IsDeliverableAsync(
            "user@such-a-domain-certainly-does-not-exist-12873.com"));

    [Theory]
    [InlineData("user@gmail.com")]
    [InlineData("user@outlook.com")]
    public async Task Real_domain_is_accepted(string email)
        => Assert.True(await Create().IsDeliverableAsync(email));
}
