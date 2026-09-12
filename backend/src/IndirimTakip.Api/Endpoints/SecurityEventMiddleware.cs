using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Security;

namespace IndirimTakip.Api.Endpoints;

internal static class SecurityEventMiddleware
{
    /// <summary>
    /// Records noteworthy requests (unauthorized attempts, rate limiting, exploit
    /// scans, server errors) in the database.
    /// </summary>
    /// <remarks>
    /// It sits EARLY in the pipeline but records AFTER <c>await next()</c>: the
    /// decision depends on the status code, which is only final once the inner
    /// layers have run. It has to be early to also see the rate limiter's 429s.
    /// </remarks>
    public static IApplicationBuilder UseSecurityEventLogging(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            await next();

            var path = context.Request.Path.Value ?? string.Empty;
            var kind = SecurityEventClassifier.Classify(context.Response.StatusCode, path);
            if (kind is null)
                return;

            var recorder = context.RequestServices.GetService<SecurityEventRecorder>();
            if (recorder is null)
                return;

            var securityEvent = new SecurityEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Ip = RequestLoggingExtensions.GetClientIp(context),
                Kind = kind,
                Method = context.Request.Method,
                Path = Truncate(path, 500) ?? "/",
                StatusCode = context.Response.StatusCode,
                UserAgent = Truncate(context.Request.Headers.UserAgent.FirstOrDefault(), 500),
                Country = Truncate(context.Request.Headers["CF-IPCountry"].FirstOrDefault(), 2),
            };

            // NO CANCELLATION TOKEN ON PURPOSE (CancellationToken.None).
            // With context.RequestAborted, an attacker who deliberately drops the
            // connection could stop their own record from being written; dropping
            // the request would become the way around the log.
            await recorder.RecordAsync(securityEvent, CancellationToken.None);
        });
    }

    // The query string is NOT stored on purpose: the path alone identifies the
    // event, while the query can carry unrelated personal data such as an email
    // address. Keeping the record narrow is part of what makes it legitimate.
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
