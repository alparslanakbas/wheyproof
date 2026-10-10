using System.Text;
using System.Text.RegularExpressions;
using IndirimTakip.Infrastructure.Scraping;
using IndirimTakip.Infrastructure.Scraping.Shopify;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Web Bot Auth signing. The primitives are pinned to the RFCs' own test vectors, and every signature
/// we produce is checked with an Ed25519 verifier: a header that is present but doesn't verify is
/// exactly what got a Shopify forum crawler treated as unsigned.
/// </summary>
public class WebBotAuthTests
{
    // RFC 9421 Appendix B.1.4, test-key-ed25519.
    private const string RfcSeed = "n4Ni-HpISpVObnQMW0wOhCKROaIKqKtW_2ZYb2p9KcU";
    private const string RfcPublicKey = "JrQLj5P_89iXES9-vFgrIy29clF9CC_oPPsw3c5D0bs";
    private const string Agent = "https://api.wheyproof.com";
    private const string Ua = "Mozilla/5.0 (compatible; wheyproofbot/1.0; +https://www.wheyproof.com/bot)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly FakeTime Time = new(new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero));

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b + new string('=', (4 - b.Length % 4) % 4));
    }

    private static WebBotAuth Auth() => new(FromBase64Url(RfcSeed), Agent, Ua, Time);

    private static bool Verifies(string publicKey, string signatureBase, string signatureBase64)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(FromBase64Url(publicKey)));
        var data = Encoding.ASCII.GetBytes(signatureBase);
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(Convert.FromBase64String(signatureBase64));
    }

    [Fact]
    public void Signing_matches_the_rfc_9421_ed25519_test_case()
    {
        // RFC 9421 Appendix B.2.6, line wrapping removed.
        var signatureBase = WebBotAuth.SignatureBase(
            [
                ("date", "Tue, 20 Apr 2021 02:07:55 GMT"), ("@method", "POST"), ("@path", "/foo"),
                ("@authority", "example.com"), ("content-type", "application/json"), ("content-length", "18"),
            ],
            "(\"date\" \"@method\" \"@path\" \"@authority\" \"content-type\" \"content-length\");created=1618884473;keyid=\"test-key-ed25519\"");

        var signature = WebBotAuth.SignBase(new Ed25519PrivateKeyParameters(FromBase64Url(RfcSeed)), signatureBase);

        Assert.Equal("wqcAqbmYJ2ji2glfAMaRy4gruYYnx2nEFN2HN6jrnDnQCK1u02Gb04v9EDgwUPiu4A0w6vuQv5lIp5WPpBKRCw==", signature);
    }

    [Fact]
    public void Keyid_is_the_rfc_8037_jwk_thumbprint()
    {
        // RFC 8037 Appendix A.3.
        Assert.Equal("kPrK_qmxVWaYVA9wwBF6Iuo3vVzz7TxHCTwXBygrS4k", WebBotAuth.Thumbprint("11qYAYKxCrfVS_7TyWQHOg7hcvPapiMlrwIaaPcHURo"));
    }

    [Fact]
    public void Public_key_and_keyid_come_from_the_seed()
    {
        var auth = Auth();

        Assert.True(auth.Enabled);
        Assert.Equal(RfcPublicKey, auth.PublicKey);
        Assert.Equal(WebBotAuth.Thumbprint(RfcPublicKey), auth.KeyId);
    }

    [Fact]
    public void A_signed_request_carries_the_three_headers_and_the_signature_verifies()
    {
        var auth = Auth();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.Kaged.com/products.json?limit=250&page=1");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0");

        auth.Sign(request);

        var agent = Assert.Single(request.Headers.GetValues("Signature-Agent"));
        var input = Assert.Single(request.Headers.GetValues("Signature-Input"));
        var signature = Assert.Single(request.Headers.GetValues("Signature"));

        // A quoted https URL, not a dictionary member (Cloudflare's guide: sig1="..." fails).
        Assert.Equal("\"https://api.wheyproof.com\"", agent);
        Assert.StartsWith("sig1=(\"@authority\" \"signature-agent\");", input);
        Assert.Contains($";keyid=\"{auth.KeyId}\"", input);
        Assert.Contains(";alg=\"ed25519\"", input);
        Assert.Contains(";tag=\"web-bot-auth\"", input);
        var created = long.Parse(Regex.Match(input, @";created=(\d+)").Groups[1].Value);
        var expires = long.Parse(Regex.Match(input, @";expires=(\d+)").Groups[1].Value);
        Assert.Equal(Time.GetUtcNow().ToUnixTimeSeconds(), created);
        Assert.Equal(60, expires - created);

        var parameters = input["sig1=".Length..];
        var signatureBase = $"\"@authority\": www.kaged.com\n\"signature-agent\": {agent}\n\"@signature-params\": {parameters}";
        var value = Regex.Match(signature, "^sig1=:(.+):$").Groups[1].Value;
        Assert.True(Verifies(auth.PublicKey!, signatureBase, value));

        Assert.Equal(Ua, request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("https://www.kaged.com/products.json", "www.kaged.com")]
    [InlineData("https://WWW.Kaged.COM/p", "www.kaged.com")]
    [InlineData("https://shop.example:8443/p", "shop.example:8443")]
    public void Authority_is_the_lowercase_host_with_a_non_default_port(string url, string expected) =>
        Assert.Equal(expected, WebBotAuth.Authority(new Uri(url)));

    [Fact]
    public void The_directory_holds_the_public_key_only_and_its_signature_verifies()
    {
        var auth = Auth();

        var (json, input, signature) = auth.Directory("api.wheyproof.com");

        Assert.Contains($"\"x\":\"{RfcPublicKey}\"", json);
        Assert.Contains("\"kty\":\"OKP\"", json);
        Assert.Contains("\"crv\":\"Ed25519\"", json);
        Assert.Contains($"\"kid\":\"{auth.KeyId}\"", json);
        Assert.DoesNotContain("\"d\"", json);
        Assert.StartsWith("sig1=(\"@authority\");", input);
        Assert.Contains(";tag=\"http-message-signatures-directory\"", input);

        var signatureBase = $"\"@authority\": api.wheyproof.com\n\"@signature-params\": {input["sig1=".Length..]}";
        Assert.True(Verifies(auth.PublicKey!, signatureBase, Regex.Match(signature, "^sig1=:(.+):$").Groups[1].Value));
    }

    // Signing off (until Shopify answers the registration): requests go out exactly as before, while
    // the directory keeps serving the key.
    [Fact]
    public void With_signing_off_requests_are_untouched_and_the_directory_is_still_served()
    {
        var auth = new WebBotAuth(FromBase64Url(RfcSeed), Agent, Ua, Time, signRequests: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.kaged.com/products.json");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0");

        auth.Sign(request);

        Assert.False(request.Headers.Contains("Signature"));
        Assert.False(request.Headers.Contains("Signature-Agent"));
        Assert.Contains("Chrome/129.0", request.Headers.UserAgent.ToString());
        Assert.True(auth.Enabled);
        Assert.Contains(RfcPublicKey, auth.Directory("api.wheyproof.com").Json);
    }

    [Fact]
    public void No_key_means_off_without_a_problem()
    {
        var auth = WebBotAuth.Create(null, Agent, Ua, Time);

        Assert.False(auth.Enabled);
        Assert.Null(auth.Problem);
    }

    [Theory]
    [InlineData("not base64!", Agent, Ua)]
    [InlineData("AAAA", Agent, Ua)]                            // 3 bytes
    [InlineData(null, "http://api.wheyproof.com", Ua)]          // seed filled in below
    [InlineData(null, "https://api.wheyproof.com/keys", Ua)]
    [InlineData(null, Agent, "")]
    public void An_unusable_setting_turns_signing_off_with_a_reason(string? seed, string agent, string ua)
    {
        var auth = WebBotAuth.Create(seed ?? Convert.ToBase64String(FromBase64Url(RfcSeed)), agent, ua, Time);

        Assert.False(auth.Enabled);
        Assert.NotNull(auth.Problem);
    }

    [Fact]
    public void The_handler_signs_listed_hosts_only()
    {
        var auth = Auth();
        var inner = new RecordingHandler();
        var client = new HttpMessageInvoker(new WebBotAuthHandler(auth, ShopifyStores.Hosts) { InnerHandler = inner });

        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://www.kaged.com/products/x"), CancellationToken.None).Wait();
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://www.myprotein.com/p/x"), CancellationToken.None).Wait();

        Assert.True(inner.Requests[0].Headers.Contains("Signature"));
        Assert.False(inner.Requests[1].Headers.Contains("Signature"));
    }

    // Huel's pages are read from its own Next.js site (see HuelScraper), not Shopify.
    [Fact]
    public void Shopify_hosts_are_the_store_addresses()
    {
        Assert.Contains("www.kaged.com", ShopifyStores.Hosts);
        Assert.DoesNotContain("huel.com", ShopifyStores.Hosts);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
