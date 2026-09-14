using IndirimTakip.Infrastructure.Subscribers;

namespace IndirimTakip.Infrastructure.Tests;

// The panel's "active" must mean exactly what the digest sends to
// (IsConfirmed && UnsubscribedAt == null); otherwise the panel would call
// someone active who never gets an email, or the other way round.
public class SubscriberStatusTests
{
    [Fact]
    public void Confirmed_and_not_unsubscribed_is_active() =>
        Assert.Equal(SubscriberStatus.Active, SubscriberService.StatusOf(true, null));

    [Fact]
    public void Unconfirmed_signup_is_pending() =>
        Assert.Equal(SubscriberStatus.Pending, SubscriberService.StatusOf(false, null));

    // Unsubscribing resets IsConfirmed, but a row where only the date is set
    // (older data, or a partial write) must still never count as active.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsubscribed_wins_regardless_of_confirmation(bool isConfirmed) =>
        Assert.Equal(SubscriberStatus.Unsubscribed, SubscriberService.StatusOf(isConfirmed, DateTimeOffset.UtcNow));
}
