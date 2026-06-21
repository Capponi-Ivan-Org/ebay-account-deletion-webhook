using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EbayAccountDeletionWebhook.Services;

public enum SignatureVerificationResult
{
    /// <summary>Signature present and valid.</summary>
    Valid,

    /// <summary>Signature missing, malformed, or it does not match the payload.</summary>
    Invalid,

    /// <summary>Verification is disabled because EBAY_CLIENT_ID/SECRET are not configured.</summary>
    SkippedNotConfigured,
}

public interface IEbayNotificationVerifier
{
    /// <summary>
    /// Verifies the <c>x-ebay-signature</c> header against the raw notification body.
    /// </summary>
    Task<SignatureVerificationResult> VerifyAsync(
        string? signatureHeader, byte[] body, CancellationToken ct = default);
}

/// <summary>
/// Verifies eBay push-notification signatures.
///
/// The <c>x-ebay-signature</c> header is Base64-encoded JSON:
///   { "alg": "ECDSA", "kid": "&lt;keyId&gt;", "signature": "&lt;base64&gt;", "digest": "SHA1" }
/// The public key is fetched from the eBay Notification API (getPublicKey) using an
/// application OAuth token, and the signature (DER-encoded) is verified with ECDSA/SHA-1
/// over the raw request body. Keys and the token are cached to stay within rate limits.
///
/// Verification is enabled only when EBAY_CLIENT_ID and EBAY_CLIENT_SECRET are set;
/// otherwise it returns <see cref="SignatureVerificationResult.SkippedNotConfigured"/>
/// so the endpoint keeps working without OAuth credentials.
/// </summary>
public sealed class EbayNotificationVerifier : IEbayNotificationVerifier
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EbayNotificationVerifier> _logger;

    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string _apiBase;

    private const string TokenCacheKey = "ebay-app-token";

    public EbayNotificationVerifier(
        HttpClient http, IMemoryCache cache, ILogger<EbayNotificationVerifier> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _clientId = Environment.GetEnvironmentVariable("EBAY_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("EBAY_CLIENT_SECRET");
        _apiBase = (Environment.GetEnvironmentVariable("EBAY_API_BASE_URL")
            ?? "https://api.ebay.com").TrimEnd('/');
    }

    public async Task<SignatureVerificationResult> VerifyAsync(
        string? signatureHeader, byte[] body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            _logger.LogWarning(
                "Signature verification disabled: EBAY_CLIENT_ID/EBAY_CLIENT_SECRET not configured.");
            return SignatureVerificationResult.SkippedNotConfigured;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("Missing x-ebay-signature header.");
            return SignatureVerificationResult.Invalid;
        }

        SignatureHeader parsed;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(signatureHeader));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            parsed = new SignatureHeader(
                Kid: root.GetProperty("kid").GetString() ?? "",
                Signature: root.GetProperty("signature").GetString() ?? "",
                Digest: root.TryGetProperty("digest", out var d) ? d.GetString() ?? "SHA1" : "SHA1");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed x-ebay-signature header.");
            return SignatureVerificationResult.Invalid;
        }

        if (string.IsNullOrEmpty(parsed.Kid) || string.IsNullOrEmpty(parsed.Signature))
        {
            _logger.LogWarning("x-ebay-signature missing kid or signature.");
            return SignatureVerificationResult.Invalid;
        }

        string? publicKeyPem;
        try
        {
            publicKeyPem = await GetPublicKeyAsync(parsed.Kid, ct);
        }
        catch (Exception ex)
        {
            // Network/auth problem retrieving the key: treat as invalid so eBay retries.
            _logger.LogError(ex, "Could not retrieve eBay public key for kid={Kid}.", parsed.Kid);
            return SignatureVerificationResult.Invalid;
        }

        if (string.IsNullOrEmpty(publicKeyPem))
        {
            return SignatureVerificationResult.Invalid;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var signatureBytes = Convert.FromBase64String(parsed.Signature);
            var hash = parsed.Digest.Equals("SHA256", StringComparison.OrdinalIgnoreCase)
                ? HashAlgorithmName.SHA256
                : HashAlgorithmName.SHA1; // eBay currently signs with SHA1
            var ok = ecdsa.VerifyData(
                body, signatureBytes, hash, DSASignatureFormat.Rfc3279DerSequence);
            if (!ok)
            {
                _logger.LogWarning("x-ebay-signature did not match the payload (kid={Kid}).", parsed.Kid);
            }
            return ok ? SignatureVerificationResult.Valid : SignatureVerificationResult.Invalid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature (kid={Kid}).", parsed.Kid);
            return SignatureVerificationResult.Invalid;
        }
    }

    private async Task<string?> GetPublicKeyAsync(string kid, CancellationToken ct)
    {
        var cacheKey = $"ebay-pubkey:{kid}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var token = await GetAppTokenAsync(ct);
        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"{_apiBase}/commerce/notification/v1/public_key/{Uri.EscapeDataString(kid)}");
        req.Headers.Authorization = new("Bearer", token);

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var key = doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;

        if (!string.IsNullOrEmpty(key))
        {
            // eBay recommends caching the key for a reasonable time (~1h).
            _cache.Set(cacheKey, key, TimeSpan.FromHours(1));
        }
        return key;
    }

    private async Task<string> GetAppTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && cachedToken is not null)
        {
            return cachedToken;
        }

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_apiBase}/identity/v1/oauth2/token");
        req.Headers.Authorization = new("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "https://api.ebay.com/oauth/api_scope",
        });

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("eBay token response had no access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 7200;

        // Cache with a safety margin so we never use a token about to expire.
        _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 300)));
        return token;
    }

    private readonly record struct SignatureHeader(string Kid, string Signature, string Digest);
}
