using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IndirimTakip.Api.Endpoints;

/// <summary>
/// Validates the signed identity token Cloudflare Access adds to requests.
/// </summary>
/// <remarks>
/// <b>WHY IT EXISTS.</b> The panel sits behind two doors: Access (at the edge,
/// email verification) and the admin key (in the application). The second door
/// is a real layer but it was tedious: the key lives only in the server's .env
/// file and the session asked for it again every 12 hours. Access already
/// verifies identity and reports it with a CRYPTOGRAPHICALLY signed token;
/// validating that token is both easier and sturdier than accepting a password
/// typed by hand. The key path was NOT removed: scripts and the `api.`
/// subdomain keep using it.
///
/// <b>FAIL-CLOSED WHEN NOT CONFIGURED.</b> Without a team domain or audience tag
/// this path is disabled entirely. The alternative, accepting the token anyway
/// with missing settings, would let a misconfiguration silently open the door.
///
/// <b>THE AUDIENCE CHECK IS REQUIRED.</b> Validating only the signature and the
/// issuer isn't enough: a valid token issued for ANOTHER application on the same
/// Cloudflare account would pass the signature check too. The `aud` tag binds
/// the token to THIS application.
/// </remarks>
public sealed class CloudflareAccessValidator
{
    public const string HeaderName = "Cf-Access-Jwt-Assertion";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<CloudflareAccessValidator> logger;
    private readonly string? issuer;
    private readonly string? audience;
    private readonly string? certsUrl;

    // The JWKS isn't downloaded on every request; Cloudflare rotates keys rarely.
    // An unknown kid triggers a refresh without waiting for the cache to expire;
    // otherwise the panel would be locked out the moment the key rotated.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyCollection<JsonWebKey> keys = [];
    private DateTimeOffset keysFetchedAt = DateTimeOffset.MinValue;

    public CloudflareAccessValidator(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudflareAccessValidator> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;

        var teamDomain = configuration["CloudflareAccess:TeamDomain"]?.Trim();
        audience = configuration["CloudflareAccess:Aud"]?.Trim();

        if (!string.IsNullOrEmpty(teamDomain) && !string.IsNullOrEmpty(audience))
        {
            issuer = "https://" + teamDomain;
            certsUrl = issuer + "/cdn-cgi/access/certs";
        }
    }

    /// <summary>Whether validation is configured.</summary>
    public bool Enabled => certsUrl is not null;

    /// <summary>
    /// True if the request carries a valid Access token.
    /// </summary>
    public async Task<bool> IsValidAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return false;

        var token = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            var valid = await ValidateAsync(token, refreshed: false, cancellationToken);

            // A signature mismatch may mean the key rotated: refresh once and retry.
            if (!valid && await RefreshKeysAsync(cancellationToken))
                valid = await ValidateAsync(token, refreshed: true, cancellationToken);

            return valid;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not validate the Cloudflare Access token.");
            return false;
        }
    }

    private async Task<bool> ValidateAsync(string token, bool refreshed, CancellationToken cancellationToken)
    {
        if (!refreshed && (keys.Count == 0 || DateTimeOffset.UtcNow - keysFetchedAt > CacheLifetime))
        {
            await RefreshKeysAsync(cancellationToken);
        }

        if (keys.Count == 0)
            return false;

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidateIssuer = true,
            // The check that binds the token to THIS application; see the remarks.
            ValidAudience = audience,
            ValidateAudience = true,
            IssuerSigningKeys = keys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            // Cloudflare uses RS256. Pinning the list rules out "alg" confusion
            // attacks (e.g. a token declaring a weak algorithm in its own header).
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromMinutes(2),
        });

        return result.IsValid;
    }

    private async Task<bool> RefreshKeysAsync(CancellationToken cancellationToken)
    {
        if (certsUrl is null)
            return false;

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var json = await client.GetStringAsync(certsUrl, cancellationToken);
            var jwks = new JsonWebKeySet(json);
            if (jwks.Keys.Count == 0)
                return false;

            keys = jwks.Keys.ToList();
            keysFetchedAt = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            // If the keys can't be downloaded, the keys ALREADY held are kept:
            // locking the panel over a transient network error is pointless.
            logger.LogWarning(ex, "Could not download the Cloudflare Access keys.");
            return false;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}
