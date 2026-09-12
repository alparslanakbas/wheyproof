using IndirimTakip.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndirimTakip.Infrastructure.Subscribers;

public record FavoriteRequest(string? Token, string? Email);

// Tells apart HOW ResolveSubscriberAsync found the subscriber. Knowing "new
// or not" isn't enough for AddAsync: when the email belongs to an existing
// subscriber (ByExistingEmail), this device has no token and the added item
// stays "invisible" (a real report: the item was added on the server, but the
// device never got a token, so the watchlist looked empty).
internal enum SubscriberResolution { ByToken, ByExistingEmail, NewlyCreated }

// The account-free watchlist. It reuses the Subscriber email + token
// infrastructure (like price alerts) but sends no email itself, so it never
// enters the confirmation flow (IsConfirmed). The token is returned on the
// first add; the frontend keeps it in localStorage for later add/remove/list
// requests.
public class FavoriteService(AppDbContext db, SubscriberService subscribers, IEmailSender emailSender)
{
    // Same reason as the confirmation email (see
    // SubscriberService.ConfirmationEmailCooldown): no rapid repeat recovery mails.
    private static readonly TimeSpan RecoveryEmailCooldown = TimeSpan.FromMinutes(5);

    // SECURITY: an earlier version returned an existing subscriber's Token to
    // anyone who merely knew the email. The Token drives confirmation,
    // unsubscribe and the watchlist, so knowing someone's email let a stranger
    // confirm (bypassing double opt-in) or cancel their subscription and read or
    // change their watchlist. The Token is now returned ONLY for a subscriber
    // actually CREATED in this call.
    // When the email already belongs to a subscriber (ByExistingEmail) the item
    // is still added but this device has no token; a recovery email is sent
    // automatically (RecoverySent=true) so the person can connect this device
    // with the same email.
    public async Task<(bool Success, string? Token, bool RecoverySent)> AddAsync(
        int productId, string? token, string? email, string frontendBaseUrl, CancellationToken cancellationToken = default)
    {
        var productExists = await db.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
            return (false, null, false);

        var (subscriber, resolution) = await ResolveSubscriberAsync(token, email, cancellationToken);
        if (subscriber is null)
            return (false, null, false);

        var alreadyFavorited = await db.ProductFavorites.AnyAsync(
            f => f.SubscriberId == subscriber.Id && f.ProductId == productId, cancellationToken);

        if (!alreadyFavorited)
        {
            db.ProductFavorites.Add(new ProductFavorite
            {
                SubscriberId = subscriber.Id,
                ProductId = productId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var recoverySent = false;
        if (resolution == SubscriberResolution.ByExistingEmail)
        {
            try
            {
                // The cooldown is checked inside SendRecoveryEmailAsync (if a real
                // recovery mail went out recently, this quietly sends nothing).
                // The item counts as added either way; a failed send mustn't
                // undo that, so the error is swallowed.
                await SendRecoveryEmailAsync(subscriber.Email, frontendBaseUrl, cancellationToken);
                recoverySent = true;
            }
            catch
            {
                // Swallowed; see the comment above.
            }
        }

        return (true, resolution == SubscriberResolution.NewlyCreated ? subscriber.Token : null, recoverySent);
    }

    public async Task<bool> RemoveAsync(int productId, string token, CancellationToken cancellationToken = default)
    {
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
        if (subscriber is null)
            return false;

        var favorite = await db.ProductFavorites.FirstOrDefaultAsync(
            f => f.SubscriberId == subscriber.Id && f.ProductId == productId, cancellationToken);
        if (favorite is null)
            return false;

        db.ProductFavorites.Remove(favorite);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<int>?> GetFavoriteProductIdsAsync(string token, CancellationToken cancellationToken = default)
    {
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
        if (subscriber is null)
            return null;

        return await db.ProductFavorites
            .Where(f => f.SubscriberId == subscriber.Id)
            .Select(f => f.ProductId)
            .ToListAsync(cancellationToken);
    }

    // For someone who lost the token on the device or browser where they saved
    // their watchlist (cleared localStorage, a different browser), a link with the
    // token goes to their email. To prevent email enumeration this method does
    // NOTHING, silently, when no subscriber is found; the caller always returns
    // the same generic message.
    public async Task SendRecoveryEmailAsync(string email, string frontendBaseUrl, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Email == normalized, cancellationToken);
        if (subscriber is null)
            return;

        if (subscriber.LastRecoveryEmailSentAt is { } lastSent && DateTimeOffset.UtcNow - lastSent < RecoveryEmailCooldown)
            return;

        // Must match the frontend route (/watchlist, which reads ?recover=).
        var recoverUrl = $"{frontendBaseUrl.TrimEnd('/')}/watchlist?recover={subscriber.Token}";
        var shieldIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "trust-shield.png");
        var confirmIconUrl = EmailTemplate.AssetUrl(frontendBaseUrl, "step-confirm.png");
        var content = $"""
            <table class="email-shell" role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:600px;max-width:600px;background:#ffffff;border:1px solid #e4e6ef;border-radius:16px;overflow:hidden;box-shadow:0 16px 44px rgba(20,24,48,.10);">
              {EmailTemplate.BrandHeader(frontendBaseUrl)}
              <tr>
                <td class="email-hero" bgcolor="#0e1122" style="padding:42px 38px;font-family:Arial,Helvetica,sans-serif;">
                  <div style="display:inline-block;border:1px solid #796cbf;background:#2b2741;color:#f5f6fb;font-size:11px;font-weight:800;line-height:16px;letter-spacing:.8px;padding:7px 12px;border-radius:999px;">WATCHLIST</div>
                  <h1 class="email-title" style="margin:20px 0 12px;color:#ffffff;font-size:34px;font-weight:800;line-height:1.1;letter-spacing:-1px;">Get your watchlist back</h1>
                  <p style="max-width:440px;margin:0;color:#c7cbe0;font-size:15px;line-height:23px;">If you can't see your saved products on this device, one click reconnects your list.</p>
                </td>
              </tr>
              <tr>
                <td class="email-pad" bgcolor="#ffffff" style="padding:30px 38px 28px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td width="64" valign="top"><img src="{EmailTemplate.Encode(confirmIconUrl)}" width="56" height="56" alt="" style="display:block;width:56px;height:56px;"></td>
                      <td valign="top" style="padding-left:12px;font-family:Arial,Helvetica,sans-serif;">
                        <div style="color:#171a2e;font-size:16px;font-weight:800;line-height:22px;">Connect this browser to your list</div>
                        <div style="margin-top:6px;color:#60667a;font-size:13px;line-height:20px;">After you click the link, your watchlist shows up on this device automatically.</div>
                      </td>
                    </tr>
                  </table>
                  <div style="margin-top:24px;text-align:center;">{EmailTemplate.PrimaryButton(recoverUrl, "Show my watchlist")}</div>
                  <p style="margin:24px 0 0;padding-top:20px;border-top:1px solid #e4e6ef;color:#60667a;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;">If you use more than one browser, open this link in each of them; every browser remembers its own list.</p>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:16px;">
                    <tr>
                      <td width="38" valign="middle"><img src="{EmailTemplate.Encode(shieldIconUrl)}" width="30" height="30" alt="" style="display:block;width:30px;height:30px;"></td>
                      <td valign="middle" style="padding-left:8px;color:#60667a;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;">If you didn't ask for this, you can ignore this email.</td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;
        var html = EmailTemplate.Document(
            "Get your watchlist back to see your saved products in this browser again.",
            content);

        await emailSender.SendAsync(subscriber.Email, "Get your watchlist back", html, cancellationToken);

        subscriber.LastRecoveryEmailSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Subscriber? Subscriber, SubscriberResolution Resolution)> ResolveSubscriberAsync(string? token, string? email, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(token))
        {
            var existing = await db.Subscribers.FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
            if (existing is not null)
                return (existing, SubscriberResolution.ByToken);
        }

        if (string.IsNullOrWhiteSpace(email))
            return (null, SubscriberResolution.ByToken);

        var normalized = email.Trim().ToLowerInvariant();
        var alreadyExisted = await db.Subscribers.AnyAsync(s => s.Email == normalized, cancellationToken);
        var subscriber = await subscribers.GetOrCreateSubscriberAsync(email, cancellationToken);
        return (subscriber, alreadyExisted ? SubscriberResolution.ByExistingEmail : SubscriberResolution.NewlyCreated);
    }
}
