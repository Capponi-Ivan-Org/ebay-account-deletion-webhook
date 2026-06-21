using EbayAccountDeletionWebhook.Models;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MarketplaceAccountDeletionFunction(ILogger<MarketplaceAccountDeletionFunction> logger)
    {
        _logger = logger;
    }

    [Function("MarketplaceAccountDeletion")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "ebay/account-deletion")]
        HttpRequest req)
    {
        if (req.Method == HttpMethods.Get)
            return HandleChallengeVerification(req);

        return await HandleAccountDeletionNotification(req);
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

    private async Task<IActionResult> HandleAccountDeletionNotification(HttpRequest req)
    {
        string body;
        try
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore lettura body");
            return new OkResult();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Body POST vuoto");
            return new OkResult();
        }

        MarketplaceAccountDeletionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MarketplaceAccountDeletionPayload>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON non valido. Body: {Body}", body);
            return new OkResult();
        }

        if (payload?.Notification?.Data == null)
        {
            _logger.LogWarning("Payload incompleto: {Body}", body);
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