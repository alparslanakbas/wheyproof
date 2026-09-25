using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace IndirimTakip.Api.Endpoints;


internal static class AdminAuthExtensions
{
    /// <summary>
    /// Protects admin endpoints: either the <c>X-Admin-Key</c> header or the
    /// admin panel's HttpOnly session cookie.
    /// </summary>
    /// <remarks>
    /// The alternative to the cookie was the panel keeping the admin key in
    /// the browser and sending it as a header on every request; that key can
    /// email every subscriber, so it must not sit where JavaScript can reach
    /// it. The header path was NOT removed: scripts, cron and manual calls
    /// use it.
    /// </remarks>
    public static RouteHandlerBuilder RequireAdminKey(this RouteHandlerBuilder builder, string? expectedKey)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            if (!await IsAuthorized(context.HttpContext, expectedKey))
                return Results.Unauthorized();

            return await RunAndRecordFailures(context, next);
        });
    }

    private static async Task<bool> IsAuthorized(HttpContext http, string? expectedKey)
    {
        if (string.IsNullOrEmpty(expectedKey))
            return false;

        // The same constant-time comparison as the sign-in endpoint. This path
        // used a plain ==, although the key can email every subscriber, so it
        // is where the protection matters most (security review, 2026-09-25).
        var providedKey = http.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (AdminSessionEndpoints.ConstantTimeEquals(providedKey, expectedKey))
            return true;

        var dataProtection = http.RequestServices.GetService<IDataProtectionProvider>();
        if (dataProtection is not null
            && AdminSessionEndpoints.IsValidSession(http, dataProtection))
        {
            return true;
        }

        // Cloudflare Access's signed identity token. Access has already
        // verified the identity and attached this to the request; validating
        // the token is sturdier than accepting a hand-typed key. When not
        // configured this path is fully closed (see CloudflareAccessValidator).
        var access = http.RequestServices.GetService<CloudflareAccessValidator>();
        return access is not null
            && await access.IsValidAsync(http, http.RequestAborted);
    }

    /// <summary>
    /// Runs the endpoint and records WHY it failed, if it did.
    /// </summary>
    /// <remarks>
    /// <b>WHY HERE.</b> This filter is the shared gate for every admin
    /// endpoint; recording here keeps it in one place and covers endpoints
    /// added later automatically.
    ///
    /// <b>THE RESPONSE BODY ISN'T READ; THE RETURNED RESULT OBJECT IS.</b>
    /// The alternative was buffering the response stream and parsing the body,
    /// a copy on every request, while <c>Results.NotFound("message")</c>
    /// already carries the message structurally.
    ///
    /// <b>UNAUTHORIZED ATTEMPTS DON'T GET HERE</b>: only authenticated
    /// requests reach this point. 401s already live in SecurityEvents, where
    /// they belong: they're attempts from outside, not admin failures.
    /// </remarks>
    private static async ValueTask<object?> RunAndRecordFailures(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        object? result;
        try
        {
            result = await next(context);
        }
        catch (Exception ex)
        {
            // A client dropping the connection isn't a failure; recording it
            // would fill the panel with noise.
            if (ex is not OperationCanceledException || !context.HttpContext.RequestAborted.IsCancellationRequested)
                await Record(context.HttpContext, StatusCodes.Status500InternalServerError, AdminFailureReason.FromException(ex));

            // Behavior DOESN'T CHANGE: the exception propagates as is.
            throw;
        }

        if (result is IStatusCodeHttpResult { StatusCode: >= 400 } status)
            await Record(context.HttpContext, status.StatusCode!.Value, ExtractMessage(result));

        return result;
    }

    private static async Task Record(HttpContext http, int statusCode, string? reason)
    {
        var recorder = http.RequestServices.GetService<AdminFailureRecorder>();
        if (recorder is null)
            return;

        var failure = new AdminOperationFailure
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Method = http.Request.Method,
            // The query string isn't stored, for the same reason as in
            // SecurityEvents: the path identifies the event.
            Path = TrimPath(http.Request.Path.Value),
            StatusCode = statusCode,
            Reason = reason,
            Ip = RequestLoggingExtensions.GetClientIp(http),
        };

        await recorder.RecordAsync(failure, CancellationToken.None);
    }

    /// <summary>The error text carried by the result object; null if none.</summary>
    private static string? ExtractMessage(object? result)
    {
        if (result is not IValueHttpResult value)
            return null;

        // ProblemDetails is unwrapped HERE, not in AdminFailureReason: that
        // type belongs to ASP.NET, while reason extraction lives in
        // Infrastructure, where the test project can see it.
        return value.Value is ProblemDetails problem
            ? AdminFailureReason.FromValue(problem.Detail ?? problem.Title)
            : AdminFailureReason.FromValue(value.Value);
    }

    private static string TrimPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        return path.Length <= 500 ? path : path[..500];
    }
}

// Logs IP + method + path + time for endpoints that send email or write data,
// so abuse leaves a trace. A separate log service or table would be
// over-engineering here; the security event log covers the persistent side.
internal static class RequestLoggingExtensions
{
    public static RouteHandlerBuilder LogSensitiveRequest(this RouteHandlerBuilder builder, ILogger logger)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var ip = GetClientIp(context.HttpContext);
            logger.LogInformation("Sensitive request: {Ip} {Method} {Path}",
                ip, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
            return await next(context);
        });
    }

    // Behind a chain of proxies, RemoteIpAddress (even after the
    // ForwardedHeaders middleware) can be an internal proxy address, losing
    // the real visitor; every request then looks like one "IP" sharing one
    // rate limit. Cloudflare's CF-Connecting-IP header exists for exactly
    // this: Cloudflare sets it at its edge and always overwrites any value a
    // client sends. Without Cloudflare (local development) we fall back to
    // RemoteIpAddress.
    public static string GetClientIp(HttpContext context)
    {
        var cfConnectingIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        return !string.IsNullOrEmpty(cfConnectingIp)
            ? cfConnectingIp
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

record VoteRequest(bool Helpful);
record RecoverFavoritesRequest(string Email);
