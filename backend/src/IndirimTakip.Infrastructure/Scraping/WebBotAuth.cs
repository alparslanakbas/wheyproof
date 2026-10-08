using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace IndirimTakip.Infrastructure.Scraping;

/// <summary>
/// Signs our requests to Shopify stores with Web Bot Auth (HTTP Message Signatures, RFC 9421) and
/// serves the public key directory that lets Shopify verify them.
/// </summary>
/// <remarks>
/// <b>WHY (2026-10-08).</b> Since 2026-09-29 Shopify answers the server's Oracle address with 429
/// (body <c>local_rate_limited</c>, even from stores we never scrape) for three of the four daily
/// cycles; the home tunnel has carried them since. Shopify's policy (changelog, effective
/// 2026-05-30): "Bots and agents that don't sign their requests are subject to the strictest
/// limits"; signed bots get higher ones, and a registration form raises them further, but only for
/// bots that already sign. Ours didn't sign and sent a browser User-Agent. A Cloudflare Worker was
/// tried first and failed: Shopify sees the original caller through it.
///
/// <b>Format</b> (Cloudflare's guide, which Shopify points to; draft-meunier-web-bot-auth-architecture
/// and draft-meunier-http-message-signatures-directory-03). Ed25519 only. Requests cover
/// <c>@authority</c> and <c>signature-agent</c>, with <c>tag="web-bot-auth"</c> and a one-minute
/// validity. <c>Signature-Agent</c> is a QUOTED https URL (a dictionary member such as
/// <c>sig1="https://..."</c> fails). <c>keyid</c> is the JWK thumbprint (RFC 8037 A.3). The
/// directory response is signed too (<c>tag="http-message-signatures-directory"</c>), over plain
/// <c>@authority</c>: the guide's own limitations section says the <c>req</c> parameter breaks it.
/// A crawler on the Shopify forum got 429 from Oracle while "signing": Shopify said its signature
/// couldn't be verified, so these details are the difference between signed and not.
///
/// <b>Key.</b> The 32-byte seed comes from the server's .env (<c>WEB_BOT_AUTH_KEY</c>, base64); it
/// was generated there and never left it. No key: off, requests go out as before. A malformed key
/// also turns it off (with <see cref="Problem"/> logged at startup) rather than stopping the site.
/// </remarks>
public sealed class WebBotAuth
{
    public const string DirectoryPath = "/.well-known/http-message-signatures-directory";
    public const string DirectoryMediaType = "application/http-message-signatures-directory+json";

    internal static readonly TimeSpan RequestValidity = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan DirectoryValidity = TimeSpan.FromHours(24);

    private readonly Ed25519PrivateKeyParameters? key;
    private readonly TimeProvider time;

    public WebBotAuth(byte[]? seed, string? signatureAgent, string? userAgent, TimeProvider time, bool signRequests = true)
    {
        this.time = time;
        SignRequests = signRequests;
        SignatureAgent = signatureAgent?.Trim() ?? "";
        UserAgent = userAgent?.Trim() ?? "";

        if (seed is null)
            return;
        if (seed.Length != Ed25519PrivateKeyParameters.KeySize)
        {
            Problem = $"the key is {seed.Length} bytes, not {Ed25519PrivateKeyParameters.KeySize}";
            return;
        }
        if (!Uri.TryCreate(SignatureAgent, UriKind.Absolute, out var agent) || agent.Scheme != Uri.UriSchemeHttps
            || agent.AbsolutePath != "/" || agent.Query.Length > 0)
        {
            Problem = $"WebBotAuth:SignatureAgent '{SignatureAgent}' is not an https origin";
            return;
        }
        if (UserAgent.Length == 0)
        {
            Problem = "WebBotAuth:UserAgent is empty";
            return;
        }

        SignatureAgent = agent.GetLeftPart(UriPartial.Authority);
        key = new Ed25519PrivateKeyParameters(seed);
        PublicKey = Base64Url(key.GeneratePublicKey().GetEncoded());
        KeyId = Thumbprint(PublicKey);
    }

    /// <summary>Built from configuration: base64 seed, Signature-Agent origin, User-Agent.</summary>
    public static WebBotAuth Create(string? seedBase64, string? signatureAgent, string? userAgent, TimeProvider time,
        bool signRequests = true)
    {
        if (string.IsNullOrWhiteSpace(seedBase64))
            return new WebBotAuth(null, signatureAgent, userAgent, time, signRequests);

        byte[] seed;
        try
        {
            seed = Convert.FromBase64String(seedBase64.Trim());
        }
        catch (FormatException)
        {
            return new WebBotAuth([], signatureAgent, userAgent, time, signRequests);
        }
        return new WebBotAuth(seed, signatureAgent, userAgent, time, signRequests);
    }

    /// <summary>A usable key is configured: the directory is served.</summary>
    public bool Enabled => key is not null;

    /// <summary>
    /// Requests are signed (and carry the bot User-Agent). Separate from <see cref="Enabled"/> because
    /// signing was switched OFF on the day it went live, with the directory left up for the registration.
    /// Measured 2026-10-08 16:47-16:52 UTC, block on: signed requests got 429 from the server as before,
    /// and through the home tunnel too (Kaged twice, Nutricost), which had passed every cycle since
    /// 2026-10-05. From the home connection in the same minutes, unsigned requests got 200 with a
    /// browser or our bot User-Agent, and so did one signed with a key NOT in our directory. Only the
    /// verifiable signature failed: Shopify recognizes the bot and holds an unregistered one to a strict
    /// limit wherever it comes from. Turned back on once Shopify answers the registration.
    /// </summary>
    public bool SignRequests { get; }

    /// <summary>Why a configured key was not used; null when on or simply not configured.</summary>
    public string? Problem { get; }

    /// <summary>The origin our requests name in Signature-Agent; the directory is served there.</summary>
    public string SignatureAgent { get; }

    /// <summary>The User-Agent sent with signed requests (Shopify cross-checks it with the registration).</summary>
    public string UserAgent { get; }

    /// <summary>Public key, base64url (the JWK "x").</summary>
    public string? PublicKey { get; }

    /// <summary>JWK SHA-256 thumbprint of the public key (RFC 8037 A.3), the "keyid".</summary>
    public string? KeyId { get; }

    /// <summary>Adds Signature-Agent, Signature-Input and Signature, and sets the bot's User-Agent.</summary>
    public void Sign(HttpRequestMessage request)
    {
        if (key is null || !SignRequests || request.RequestUri is null)
            return;

        var created = time.GetUtcNow().ToUnixTimeSeconds();
        var expires = created + (long)RequestValidity.TotalSeconds;
        var agent = $"\"{SignatureAgent}\"";
        var parameters = $"(\"@authority\" \"signature-agent\");created={created};keyid=\"{KeyId}\";alg=\"ed25519\""
                         + $";expires={expires};nonce=\"{Nonce()}\";tag=\"web-bot-auth\"";
        var signature = SignBase(key, SignatureBase([("@authority", Authority(request.RequestUri)), ("signature-agent", agent)], parameters));

        var headers = request.Headers;
        headers.Remove("Signature-Agent");
        headers.Remove("Signature-Input");
        headers.Remove("Signature");
        headers.TryAddWithoutValidation("Signature-Agent", agent);
        headers.TryAddWithoutValidation("Signature-Input", $"sig1={parameters}");
        headers.TryAddWithoutValidation("Signature", $"sig1=:{signature}:");
        headers.UserAgent.Clear();
        headers.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <summary>The key directory (JWKS) and its signature headers for a request to <paramref name="authority"/>.</summary>
    public (string Json, string SignatureInput, string Signature) Directory(string authority)
    {
        if (key is null)
            throw new InvalidOperationException("Web Bot Auth is off.");

        var json = JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "OKP", crv = "Ed25519", kid = KeyId, x = PublicKey, use = "sig" } },
        });
        var created = time.GetUtcNow().ToUnixTimeSeconds();
        var parameters = $"(\"@authority\");alg=\"ed25519\";created={created};expires={created + (long)DirectoryValidity.TotalSeconds}"
                         + $";keyid=\"{KeyId}\";nonce=\"{Nonce()}\";tag=\"http-message-signatures-directory\"";
        var signature = SignBase(key, SignatureBase([("@authority", authority.Trim().ToLowerInvariant())], parameters));
        return (json, $"sig1={parameters}", $"sig1=:{signature}:");
    }

    /// <summary>RFC 9421 section 2.5: one line per covered component, then the parameters; no final newline.</summary>
    internal static string SignatureBase(IReadOnlyList<(string Id, string Value)> components, string parameters)
    {
        var lines = components.Select(c => $"\"{c.Id}\": {c.Value}").Append($"\"@signature-params\": {parameters}");
        return string.Join("\n", lines);
    }

    internal static string SignBase(Ed25519PrivateKeyParameters key, string signatureBase)
    {
        var data = Encoding.ASCII.GetBytes(signatureBase);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    /// <summary>RFC 9421 section 2.2.3: lowercase host, port only when not the scheme's default.</summary>
    internal static string Authority(Uri uri) =>
        (uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}").ToLowerInvariant();

    /// <summary>RFC 7638 over the required members in lexical order (RFC 8037 A.3 for OKP keys).</summary>
    internal static string Thumbprint(string x) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"Ed25519\",\"kty\":\"OKP\",\"x\":\"{x}\"}}")));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Nonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
