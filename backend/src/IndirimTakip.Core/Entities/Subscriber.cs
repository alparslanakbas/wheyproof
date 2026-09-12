namespace IndirimTakip.Core.Entities;

// Email newsletter subscribers. Double opt-in is required: a subscriber is
// created with IsConfirmed=false and switched to true after clicking the
// confirmation link carrying the Token. The same Token is used in both the
// confirmation and the unsubscribe links.
public class Subscriber
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string Token { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTimeOffset SubscribedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? UnsubscribedAt { get; set; }
    public DateTimeOffset? LastConfirmationEmailSentAt { get; set; }
    // Separate cooldown for the watchlist recovery email (see
    // FavoriteService.SendRecoveryEmailAsync): same purpose as the confirmation
    // email but a different flow, kept apart so neither resets the other.
    public DateTimeOffset? LastRecoveryEmailSentAt { get; set; }
    // When the digest last went to this subscriber. This field is the ONLY
    // source of scheduling, not an in-memory counter, which reset on every
    // deploy/restart so the digest never went out. It also answers "who hasn't
    // received this week's digest yet", so when the daily sending quota runs
    // out, the remaining subscribers continue the next day where it stopped
    // (see DigestService).
    public DateTimeOffset? LastDigestSentAt { get; set; }
}
