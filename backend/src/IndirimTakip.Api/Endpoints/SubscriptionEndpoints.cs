using IndirimTakip.Core.Scraping;
using IndirimTakip.Infrastructure;
using IndirimTakip.Infrastructure.Articles;
using IndirimTakip.Infrastructure.Coupons;
using IndirimTakip.Infrastructure.Deals;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Subscribers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IndirimTakip.Api.Endpoints;

// Newsletter subscription: sign-up, email confirmation and unsubscribe.
internal static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this WebApplication app, string frontendBaseUrl)
    {
        // Newsletter: double opt-in is required. This endpoint never activates a
        // subscriber directly; it only triggers the confirmation email.
        app.MapPost("/api/subscribe", async (SubscribeRequest request, SubscriberService subscribers,
            EmailAddressValidator emailValidator, HttpContext http, CancellationToken ct) =>
        {
            // A filled honeypot means a bot made the request. No error is returned:
            // the bot shouldn't learn which check caught it, and a real person
            // never reaches this branch.
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                app.Logger.LogInformation("Ignored a subscription request with a filled honeypot: {Ip}",
                    RequestLoggingExtensions.GetClientIp(http));
                return Results.Ok(new { message = "Check your inbox; we sent you a confirmation link." });
            }

            if (!EndpointHelpers.IsValidEmail(request.Email))
                return Results.BadRequest(new { message = "Enter a valid email address." });

            // Does the domain really exist? Sending confirmations to made-up
            // addresses eats the quota, and bounces hurt the sender's reputation.
            if (!await emailValidator.IsDeliverableAsync(request.Email, ct))
                return Results.BadRequest(new { message = "We can't reach that email address. Could you check it?" });

            var confirmBaseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var sent = await subscribers.SubscribeAsync(request, confirmBaseUrl, ct);
            if (!sent)
                return Results.Json(new { message = "We can't send the confirmation email right now. Please try again in a moment." }, statusCode: StatusCodes.Status502BadGateway);
            return Results.Ok(new { message = "Check your inbox; we sent you a confirmation link." });
        }).RequireRateLimiting("EmailSensitive").LogSensitiveRequest(app.Logger);

        // Confirm/unsubscribe links are clicked straight from email, so they return
        // a simple HTML page, not JSON; a separate frontend route for these static
        // messages would be overkill. charset=utf-8 is set explicitly so the
        // browser never guesses the encoding.
        app.MapGet("/api/subscribe/confirm/{token}", async (string token, SubscriberService subscribers, CancellationToken ct) =>
        {
            var success = await subscribers.ConfirmAsync(token, ct);
            var html = success
                ? EndpointHelpers.BuildSubscriptionConfirmedPage(frontendBaseUrl)
                : EndpointHelpers.BuildInfoPage("This link isn't valid.", "The confirmation link may have expired or already been used.", frontendBaseUrl);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/api/subscribe/unsubscribe/{token}", async (string token, SubscriberService subscribers, CancellationToken ct) =>
        {
            var success = await subscribers.UnsubscribeAsync(token, ct);
            var html = success
                ? EndpointHelpers.BuildInfoPage("You're unsubscribed.", "If you change your mind, you can subscribe again anytime.", frontendBaseUrl)
                : EndpointHelpers.BuildInfoPage("This link isn't valid.", "The link may have expired or already been used.", frontendBaseUrl);
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
}
