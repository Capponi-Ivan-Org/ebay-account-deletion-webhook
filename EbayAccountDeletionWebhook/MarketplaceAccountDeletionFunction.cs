using EbayAccountDeletionWebhook.Models;
using EbayAccountDeletionWebhook.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EbayAccountDeletionWebhook.Functions;

public class MarketplaceAccountDeletionFunction
{
    private readonly ILogger<MarketplaceAccountDeletionFunction> _logger;
    private readonly IEbayNotificationVerifier _verifier;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MarketplaceAccountDeletionFunction(
        ILogger<MarketplaceAccountDeletionFunction> logger, IEbayNotificationVerifier verifier)
    {
        _logger = logger;
        _verifier = verifier;
    }

    [Function("MarketplaceAccountDeletion")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "ebay/account-deletion")]
        HttpRequest req,
        CancellationToken ct)
    {
        if (req.Method == HttpMethods.Get)
            return HandleChallengeVerification(req);

        return await HandleAccountDeletionNotification(req, ct);
    }

    private IActionResult HandleChallengeVerification(HttpRequest req)
    {
        var challengeCode = req.Query["challenge_code"].ToString();

        if (string.IsNullOrEmpty(challengeCode))
        {
            _logger.LogWarning("GET senza challenge_code");
            return new BadRequestObjectResult("challenge_code mancante");
        }

        // Letti ad ogni chiamata: i static readonly su Flex Consumption
        // vengono inizializzati prima che le App Settings siano disponibili
        var verificationToken = Environment.GetEnvironmentVariable("EBAY_VERIFICATION_TOKEN");
        var endpointUrl = Environment.GetEnvironmentVariable("EBAY_ENDPOINT_URL");

        if (string.IsNullOrEmpty(verificationToken) || string.IsNullOrEmpty(endpointUrl))
        {
            _logger.LogError(
                "Variabili mancanti — TOKEN={Token} URL={Url}",
                string.IsNullOrEmpty(verificationToken) ? "MANCANTE" : "ok",
                string.IsNullOrEmpty(endpointUrl) ? "MANCANTE" : "ok");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        _logger.LogInformation(
            "Challenge ricevuto | Code={Code} | Token={Token} | Url={Url}",
            challengeCode,
            verificationToken[..4] + "****",   // logga solo i primi 4 char per sicurezza
            endpointUrl);

        // Ordine OBBLIGATORIO: challengeCode + verificationToken + endpoint
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(Encoding.UTF8.GetBytes(challengeCode));
        sha256.AppendData(Encoding.UTF8.GetBytes(verificationToken));
        sha256.AppendData(Encoding.UTF8.GetBytes(endpointUrl));
        var hashHex = BitConverter.ToString(sha256.GetHashAndReset())
                                  .Replace("-", string.Empty)
                                  .ToLower();

        _logger.LogInformation("Challenge response: {Hash}", hashHex);

        return new OkObjectResult(new ChallengeVerificationResponse { Value = hashHex });
    }

    private async Task<IActionResult> HandleAccountDeletionNotification(HttpRequest req, CancellationToken ct)
    {
        // Read the raw body bytes: the signature is computed over the exact bytes received.
        byte[] body;
        try
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            body = ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore lettura body");
            return new OkResult();
        }

        if (body.Length == 0)
        {
            _logger.LogWarning("Body POST vuoto");
            return new OkResult();
        }

        // Verify the x-ebay-signature before trusting the payload.
        var signature = req.Headers["x-ebay-signature"].FirstOrDefault();
        var verification = await _verifier.VerifyAsync(signature, body, ct);
        if (verification == SignatureVerificationResult.Invalid)
        {
            _logger.LogWarning("Notifica rifiutata: x-ebay-signature non valida");
            return new StatusCodeResult(StatusCodes.Status412PreconditionFailed);
        }

        MarketplaceAccountDeletionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MarketplaceAccountDeletionPayload>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON non valido ({Bytes} byte)", body.Length);
            return new OkResult();
        }

        if (payload?.Notification?.Data == null)
        {
            _logger.LogWarning("Payload incompleto ({Bytes} byte)", body.Length);
            return new OkResult();
        }

        var notif = payload.Notification;
        var data = notif.Data;

        _logger.LogInformation(
            "Account deletion | NotificationId={Id} | Attempt={Attempt} | UserId={UserId}",
            notif.NotificationId, notif.PublishAttemptCount, data.UserId);

        _ = ProcessAccountDeletionAsync(data.UserId, data.Username, data.EiasToken);

        return new OkResult();
    }

    private async Task ProcessAccountDeletionAsync(
        string? userId, string? username, string? eiasToken)
    {
        try
        {
            _logger.LogInformation(
                "Elaborazione cancellazione: UserId={UserId} Username={Username}",
                userId, username);

            // TODO: cancella dai tuoi store
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore ProcessAccountDeletion UserId={UserId}", userId);
        }
    }
}