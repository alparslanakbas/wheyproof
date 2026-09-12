using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Subscribers;

// Brevo's transactional email API (a permanent free tier with a daily limit). The
// sender address requires the domain to be verified in Brevo with SPF/DKIM;
// otherwise emails may land in spam or not be sent at all.
public class BrevoEmailSender(HttpClient httpClient, IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Brevo:ApiKey"];
        var senderEmail = configuration["Brevo:SenderEmail"] ?? "newsletter@wheyproof.com";

        var payload = new
        {
            sender = new { name = "WheyProof", email = senderEmail },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("api-key", apiKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
