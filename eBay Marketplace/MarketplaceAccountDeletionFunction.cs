using eBay_Marketplace.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace eBay_Marketplace.Functions;

/// <summary>
/// Gestisce le notifiche di cancellazione account eBay Marketplace.
///
/// GET  /api/ebay/account-deletion?challenge_code=xxx  → verifica endpoint
/// POST /api/ebay/account-deletion                     → notifica cancellazione
/// </summary>
public class MarketplaceAccountDeletionFunction
{
    private readonly ILogger<MarketplaceAccountDeletionFunction> _logger;

    private static readonly string VerificationToken =
        Environment.GetEnvironmentVariable("EBAY_VERIFICATION_TOKEN") ?? string.Empty;

    private static readonly string EndpointUrl =
        Environment.GetEnvironmentVariable("EBAY_ENDPOINT_URL") ?? string.Empty;

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

    // ────────────────────────────────────────────────────────────────────────
    // GET ?challenge_code=xxx
    // SHA-256(challengeCode + verificationToken + endpointUrl) → hex lowercase
    // Il content-type DEVE essere application/json (gestito da OkObjectResult)
    // ────────────────────────────────────────────────────────────────────────
    private IActionResult HandleChallengeVerification(HttpRequest req)
    {
        var challengeCode = req.Query["challenge_code"].ToString();

        if (string.IsNullOrEmpty(challengeCode))
        {
            _logger.LogWarning("GET senza challenge_code");
            return new BadRequestObjectResult("challenge_code mancante");
        }

        if (string.IsNullOrEmpty(VerificationToken) || string.IsNullOrEmpty(EndpointUrl))
        {
            _logger.LogError("EBAY_VERIFICATION_TOKEN o EBAY_ENDPOINT_URL non configurati");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        // Ordine OBBLIGATORIO: challengeCode + verificationToken + endpoint
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(Encoding.UTF8.GetBytes(challengeCode));
        sha256.AppendData(Encoding.UTF8.GetBytes(VerificationToken));
        sha256.AppendData(Encoding.UTF8.GetBytes(EndpointUrl));
        var hashHex = BitConverter.ToString(sha256.GetHashAndReset())
                                  .Replace("-", string.Empty)
                                  .ToLower();

        _logger.LogInformation("Challenge OK: {Code}", challengeCode);

        // OkObjectResult serializza con JsonSerializer → nessun BOM, content-type application/json
        return new OkObjectResult(new ChallengeVerificationResponse { Value = hashHex });
    }

    // ────────────────────────────────────────────────────────────────────────
    // POST — notifica cancellazione account
    // Risponde subito 200, poi elabora (eBay ritrasmette se non riceve 2xx)
    // ────────────────────────────────────────────────────────────────────────
    private async Task<IActionResult> HandleAccountDeletionNotification(HttpRequest req)
    {
        // Leggi body
        string body;
        try
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore lettura body");
            return new BadRequestResult();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Body POST vuoto");
            return new OkResult(); // rispondi comunque 200 per non far ritrasmettere
        }

        // Deserializza
        MarketplaceAccountDeletionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MarketplaceAccountDeletionPayload>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON non valido. Body: {Body}", body);
            return new OkResult(); // 200 comunque: errore nostro, non di eBay
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

        // Elaborazione asincrona: non bloccare la risposta
        _ = ProcessAccountDeletionAsync(data.UserId, data.Username, data.EiasToken);

        // eBay accetta: 200, 201, 202, 204
        return new OkResult();
    }

    /// <summary>
    /// Rimuovi/anonimizza i dati dell'utente.
    /// GDPR/CCPA: entro 30 giorni dalla notifica.
    /// </summary>
    private async Task ProcessAccountDeletionAsync(
        string? userId, string? username, string? eiasToken)
    {
        try
        {
            _logger.LogInformation(
                "Elaborazione cancellazione: UserId={UserId} Username={Username}",
                userId, username);

            // TODO: cancella dai tuoi store, ad esempio:
            // await _db.Users.Where(u => u.EbayUserId == userId).ExecuteDeleteAsync();
            // await _serviceBus.SendMessageAsync(...);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // Non propagare: la risposta 200 è già stata inviata a eBay.
            // Logga e gestisci con retry interno o dead-letter queue.
            _logger.LogError(ex, "Errore durante ProcessAccountDeletion per UserId={UserId}", userId);
        }
    }
}