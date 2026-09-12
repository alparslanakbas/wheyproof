using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Subscribers;

public record SubscribeRequest(
    string Email,
    // Honeypot: a field hidden in the form that a real person never sees.
    // Autofilling bots fill it too; a request with it filled is silently
    // ignored (no error, so the bot doesn't learn which check caught it).
    string? Website = null);

// Double opt-in is required: a subscribe request doesn't activate anything,
// IsConfirmed stays false until the confirmation link is clicked. An already
// confirmed address subscribing again is quietly ignored (no repeat mail).
public class SubscriberService(
    AppDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<SubscriberService> logger)
{
    // /api/subscribe and /api/products/{id}/watch used to send a confirmation
    // email EVERY TIME they were called for an unconfirmed subscriber. Called
    // repeatedly with the same address (a bot or abuse), dozens of mails went out
    // within a minute and the provider blocklisted the address. This cooldown is
    // a layer on top of the IP rate limit (Program.cs): even if the limit is
    // dodged by switching IPs, the same address gets no second mail right away.
    private static readonly TimeSpan ConfirmationEmailCooldown = TimeSpan.FromMinutes(5);

    // Returns whether the confirmation email could really be sent (already
    // confirmed counts as true: nothing to send). On false the caller must
    // return an honest "can't send right now" error, NOT a misleading "check
    // your inbox"; otherwise the person waits forever for a link that never comes.
    public async Task<bool> SubscribeAsync(SubscribeRequest request, string confirmBaseUrl, CancellationToken cancellationToken = default)
    {
        var subscriber = await GetOrCreateSubscriberAsync(request.Email, cancellationToken);
        if (subscriber.IsConfirmed)
            return true;

        return await SendConfirmationEmailAsync(subscriber, confirmBaseUrl, cancellationToken);
    }

    // Other flows (price alerts) also need to find or create a subscriber:
    // the same Subscriber table and confirmation process, no separate consent.
    public async Task<Subscriber> GetOrCreateSubscriberAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Email == normalized, cancellationToken);
        if (subscriber is null)
        {
            subscriber = new Subscriber
            {
                Email = normalized,
                Token = Guid.NewGuid().ToString("N"),
                SubscribedAt = DateTimeOffset.UtcNow,
            };
            db.Subscribers.Add(subscriber);
            await db.SaveChangesAsync(cancellationToken);
        }

        return subscriber;
    }

    public async Task<bool> SendConfirmationEmailAsync(Subscriber subscriber, string confirmBaseUrl, CancellationToken cancellationToken = default)
    {
        if (subscriber.LastConfirmationEmailSentAt is { } lastSent && DateTimeOffset.UtcNow - lastSent < ConfirmationEmailCooldown)
            return true;

        var confirmUrl = $"{confirmBaseUrl}/api/subscribe/confirm/{subscriber.Token}";
        var frontendBaseUrl = configuration["FrontendBaseUrl"] ?? EmailTemplate.ProductionFrontendUrl;
        var html = BuildConfirmationHtml(confirmUrl, frontendBaseUrl);

        // The email provider can be temporarily unavailable (wrong or expired API
        // key, network trouble, its own downtime). Rather than a bare 500, the
        // error is caught here so the caller can give an honest message, and
        // LastConfirmationEmailSentAt is ONLY updated when sending really worked
        // (otherwise the person would wait 5 minutes for a mail that never left).
        try
        {
            await emailSender.SendAsync(subscriber.Email, "WheyProof: confirm your subscription", html, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Confirmation email could not be sent: {Email}", subscriber.Email);
            return false;
        }

        subscriber.LastConfirmationEmailSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string BuildConfirmationHtml(string confirmUrl, string frontendBaseUrl)
    {
        var signalImageUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "confirmation-price-signal.jpg");
        var confirmIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "step-confirm.png");
        var alertIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "step-alert.png");
        var shieldIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "trust-shield.png");
        var siteLabel = new Uri(frontendBaseUrl).Host.Replace("www.", string.Empty);

        var content = $"""
            <table class="email-shell" role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:600px;max-width:600px;background:#ffffff;border:1px solid #e4e6ef;border-radius:16px;overflow:hidden;box-shadow:0 16px 44px rgba(20,24,48,.10);">
              {EmailTemplate.BrandHeader(frontendBaseUrl)}
              <tr>
                <td class="email-hero" background="{EmailTemplate.Encode(signalImageUrl)}" bgcolor="#0e1122" style="padding:52px 42px 48px;background-color:#0e1122;background-image:url('{EmailTemplate.Encode(signalImageUrl)}');background-position:center;background-size:cover;background-repeat:no-repeat;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td width="70%" style="width:70%;font-family:Arial,Helvetica,sans-serif;">
                        <div style="display:inline-block;border:1px solid #796cbf;background:#2b2741;color:#f5f6fb;font-size:11px;font-weight:800;line-height:16px;letter-spacing:.8px;padding:7px 12px;border-radius:999px;">CONFIRM YOUR EMAIL</div>
                        <h1 class="email-title" style="margin:24px 0 14px;color:#ffffff;font-size:35px;font-weight:800;line-height:1.08;letter-spacing:-1.1px;">One last step<br>to real deals</h1>
                        <p style="margin:0 0 24px;color:#c7cbe0;font-size:16px;line-height:1.55;">Confirm your email address so you don't miss the week's top price drops.</p>
                        {EmailTemplate.PrimaryButton(confirmUrl, "Confirm my subscription")}
                      </td>
                      <td width="30%" class="mobile-hide" style="width:30%;">&nbsp;</td>
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td class="email-pad" style="padding:30px 38px 28px;background:#ffffff;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td class="step-cell" width="50%" valign="top" style="width:50%;padding:4px 18px 18px 0;">
                        <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td valign="top" width="58"><img src="{EmailTemplate.Encode(confirmIconUrl)}" width="54" height="54" alt="" style="display:block;width:54px;height:54px;"></td>
                            <td valign="top" style="padding-left:10px;font-family:Arial,Helvetica,sans-serif;">
                              <div style="color:#171a2e;font-size:15px;font-weight:800;line-height:21px;">1. Confirm</div>
                              <div style="margin-top:5px;color:#60667a;font-size:13px;line-height:19px;">Confirm your email address to secure your subscription.</div>
                            </td>
                          </tr>
                        </table>
                      </td>
                      <td class="step-cell" width="50%" valign="top" style="width:50%;padding:4px 0 18px 18px;">
                        <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td valign="top" width="58"><img src="{EmailTemplate.Encode(alertIconUrl)}" width="54" height="54" alt="" style="display:block;width:54px;height:54px;"></td>
                            <td valign="top" style="padding-left:10px;font-family:Arial,Helvetica,sans-serif;">
                              <div style="color:#171a2e;font-size:15px;font-weight:800;line-height:21px;">2. Get the deals</div>
                              <div style="margin-top:5px;color:#60667a;font-size:13px;line-height:19px;">The week's top price drops land in your inbox.</div>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                  <div style="height:1px;background:#e4e6ef;margin:2px 0 20px;line-height:1px;">&nbsp;</div>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td width="44" valign="middle"><img src="{EmailTemplate.Encode(shieldIconUrl)}" width="38" height="38" alt="" style="display:block;width:38px;height:38px;"></td>
                      <td valign="middle" style="padding-left:10px;font-family:Arial,Helvetica,sans-serif;color:#303548;font-size:12px;line-height:18px;">
                        If you didn't ask for this, you can ignore this email.<br>
                        <a href="{EmailTemplate.Encode(frontendBaseUrl)}" style="color:#6556e8;font-weight:700;text-decoration:none;">{EmailTemplate.Encode(siteLabel)}</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;

        return EmailTemplate.Document(
            "Confirm your subscription in one click to get real price drops.",
            content);
    }

    public async Task<bool> ConfirmAsync(string token, CancellationToken cancellationToken = default)
    {
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
        if (subscriber is null)
            return false;

        subscriber.IsConfirmed = true;
        subscriber.ConfirmedAt = DateTimeOffset.UtcNow;
        subscriber.UnsubscribedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnsubscribeAsync(string token, CancellationToken cancellationToken = default)
    {
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
        if (subscriber is null)
            return false;

        // IsConfirmed is reset too: otherwise the "already confirmed" shortcut in
        // SubscribeAsync would kick in on a later re-subscribe and no new
        // confirmation mail would ever go out.
        subscriber.UnsubscribedAt = DateTimeOffset.UtcNow;
        subscriber.IsConfirmed = false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
