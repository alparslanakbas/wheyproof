using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Subscribers;

/// <summary>
/// Checks an email address's format and whether its domain really exists.
///
/// A format check alone wasn't enough: made-up but well-formed addresses
/// (someone typing a message into the form, or a bot) triggered confirmation
/// emails. That eats into the quota, and bounced mail lowers the sender's
/// reputation.
/// </summary>
public class EmailAddressValidator(ILogger<EmailAddressValidator> logger)
{
    /// <summary>
    /// Disposable mail providers. A long list is pointless (new ones keep
    /// appearing); only the common ones.
    /// </summary>
    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "10minutemail.com", "tempmail.com",
        "temp-mail.org", "yopmail.com", "throwawaymail.com", "getnada.com",
        "trashmail.com", "sharklasers.com", "maildrop.cc", "dispostable.com",
    };

    /// <summary>
    /// Upper bound for domain resolution. Short so a real user isn't kept waiting;
    /// when exceeded the address is ACCEPTED (see below).
    /// </summary>
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(3);

    public async Task<bool> IsDeliverableAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // MailAddress accepts addresses containing spaces (because of the quoted
        // local part rule); in practice such an address is always a typo or junk.
        if (email.Any(char.IsWhiteSpace))
            return false;

        string domain;
        try
        {
            var parsed = new MailAddress(email.Trim());
            domain = parsed.Host;
        }
        catch (FormatException)
        {
            return false;
        }

        if (domain.Length == 0 || !domain.Contains('.'))
            return false;

        if (DisposableDomains.Contains(domain))
            return false;

        return await DomainResolvesAsync(domain, cancellationToken);
    }

    /// <summary>
    /// Checks whether the domain resolves.
    ///
    /// If DNS itself errors or times out, the address is ACCEPTED (fail-open):
    /// blocking a real user's subscription over a transient network issue is
    /// worse than accepting a few fake addresses.
    /// </summary>
    private async Task<bool> DomainResolvesAsync(string domain, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DnsTimeout);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, timeout.Token);
            return addresses.Length > 0;
        }
        catch (SocketException)
        {
            // The domain doesn't exist: exactly the case we're looking for.
            return false;
        }
        catch (Exception e) when (e is OperationCanceledException or ArgumentException)
        {
            logger.LogInformation("Domain could not be resolved; accepting the address: {Domain}", domain);
            return true;
        }
    }
}
