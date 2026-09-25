using System.Security.Cryptography;
using System.Text;
using IndirimTakip.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace IndirimTakip.Api.Endpoints;

/// <summary>
/// Sign-in and sign-out endpoints for the admin panel.
/// </summary>
/// <remarks>
/// <b>THE KEY IS NOT STORED IN THE BROWSER.</b> The panel takes the admin key
/// once, the server verifies it and sets an HttpOnly cookie, and the key is
/// never written anywhere again. Keeping the key in localStorage would be
/// the easy path, but that key can email EVERY SUBSCRIBER; sitting where
/// JavaScript can read it, a single XSS would hand over the newsletter.
/// JavaScript can't read an HttpOnly cookie.
///
/// <b>No server-side session; a signed token instead.</b> Data Protection
/// with a time limit: the token carries its own expiry, so no table and no
/// cleanup. The side effect is deliberate: the keys live in the container,
/// so every deploy requires signing in again. Not a flaw but a cheap
/// security feature: a session left open closes itself on deploy.
/// </remarks>
internal static class AdminSessionEndpoints
{
    public const string CookieName = "wp_admin";
    private const string ProtectorPurpose = "wheyproof.admin.session";
    private const string SessionPayload = "admin";
    // The panel lives at /admin; the cookie is only sent on that path (its
    // API included, via /admin/api). Changing the panel's route means
    // changing this too, or sign-in "works" and every call returns 401.
    private const string CookiePath = "/admin";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    public static void MapAdminSession(this WebApplication app, string? adminApiKey)
    {
        // [FromServices] is REQUIRED, don't remove it. With a body parameter
        // present, Minimal API CAN'T infer IDataProtectionProvider as a
        // service and fails with "Failure to infer one or more parameters".
        // The failure shows up not at compile time but when endpoints are
        // built, and it takes down the whole endpoint list: EVERY API
        // endpoint returns 500.
        app.MapPost("/api/dev/session", (
            SignInRequest request,
            [FromServices] IDataProtectionProvider dataProtection,
            HttpContext context) =>
        {
            if (string.IsNullOrEmpty(adminApiKey) || !ConstantTimeEquals(request.Key, adminApiKey))
            {
                // A 401, and it IS RECORDED: SecurityEventMiddleware already
                // writes 401s, so anyone trying to get into the panel shows up
                // in the panel's own event feed with their address.
                return Results.Unauthorized();
            }

            var token = Protector(dataProtection).Protect(SessionPayload, SessionLifetime);

            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,   // not readable by JavaScript
                Secure = true,     // HTTPS only
                SameSite = SameSiteMode.Strict,
                // Sent only on the panel's path; the rest of the site never
                // sees this cookie.
                Path = CookiePath,
                MaxAge = SessionLifetime,
                IsEssential = true,
            });

            return Results.Ok(new { ok = true });
        })
        // The limit that makes guessing useless. The endpoint's name is
        // guessable too, so a loose limit here would be pointless.
        .RequireRateLimiting("admin-login");

        app.MapDelete("/api/dev/session", (HttpContext context) =>
        {
            context.Response.Cookies.Delete(CookieName, new CookieOptions
            {
                Path = CookiePath,
                Secure = true,
                SameSite = SameSiteMode.Strict,
            });
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>True when the session token in the cookie is valid.</summary>
    public static bool IsValidSession(HttpContext context, IDataProtectionProvider dataProtection)
    {
        var token = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            return Protector(dataProtection).Unprotect(token) == SessionPayload;
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, or the key changed after a deploy.
            return false;
        }
    }

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider) =>
        provider.CreateProtector(ProtectorPurpose).ToTimeLimitedDataProtector();

    // Hashed first so a length difference leaks nothing either:
    // FixedTimeEquals returns early for arrays of different lengths, which
    // would let someone measure the key's length.
    internal static bool ConstantTimeEquals(string? given, string expected)
    {
        if (string.IsNullOrEmpty(given))
            return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    internal record SignInRequest(string? Key);
}
