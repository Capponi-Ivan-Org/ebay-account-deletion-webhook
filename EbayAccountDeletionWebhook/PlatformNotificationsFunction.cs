using EbayAccountDeletionWebhook.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EbayAccountDeletionWebhook;

/// <summary>
/// Gestisce le Platform Notifications push di eBay (ITEM_SOLD, FEEDBACK_LEFT, ecc.)
///
/// GET  /api/ebay/notifications?challenge_code=xxx  → verifica endpoint
/// POST /api/ebay/notifications                     → notifica evento
/// </summary>
public class PlatformNotificationsFunction
{
    private readonly ILogger<PlatformNotificationsFunction> _logger;

    private static readonly string VerificationToken =
        Environment.GetEnvironmentVariable("EBAY_VERIFICATION_TOKEN") ?? string.Empty;

    private static readonly string EndpointUrl =
        Environment.GetEnvironmentVariable("EBAY_PLATFORM_ENDPOINT_URL") ?? string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PlatformNotificationsFunction(ILogger<PlatformNotificationsFunction> logger)
    {
        _logger = logger;
    }

    [Function("PlatformNotifications")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "ebay/notifications")]
        HttpRequest req)
    {
        if (req.Method == HttpMethods.Get)
            return HandleChallengeVerification(req);

        return await HandlePlatformNotification(req);
    }

    // ────────────────────────────────────────────────────────────────────────
    // GET ?challenge_code=xxx
    // Ordine OBBLIGATORIO: challengeCode + verificationToken + endpoint
    // ────────────────────────────────────────────────────────────────────────
    private IActionResult HandleChallengeVerification(HttpRequest req)
    {
        var challengeCode = req.Query["challenge_code"].ToString();

        if (string.IsNullOrEmpty(challengeCode))
            return new BadRequestObjectResult("challenge_code mancante");

        if (string.IsNullOrEmpty(VerificationToken) || string.IsNullOrEmpty(EndpointUrl))
        {
            _logger.LogError("EBAY_VERIFICATION_TOKEN o EBAY_PLATFORM_ENDPOINT_URL non configurati");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(Encoding.UTF8.GetBytes(challengeCode));
        sha256.AppendData(Encoding.UTF8.GetBytes(VerificationToken));
        sha256.AppendData(Encoding.UTF8.GetBytes(EndpointUrl));
        var hashHex = BitConverter.ToString(sha256.GetHashAndReset())
                                  .Replace("-", string.Empty)
                                  .ToLower();

        _logger.LogInformation("Challenge OK: {Code}", challengeCode);
        return new OkObjectResult(new ChallengeVerificationResponse { Value = hashHex });
    }

    // ────────────────────────────────────────────────────────────────────────
    // POST — notifica evento eBay
    // ────────────────────────────────────────────────────────────────────────
    private async Task<IActionResult> HandlePlatformNotification(HttpRequest req)
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
            return new OkResult(); // 200 comunque per non far ritrasmettere
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

        if (payload?.Notification == null)
        {
            _logger.LogWarning("Payload incompleto: {Body}", body);
            return new OkResult();
        }

        var topic = payload.Metadata?.Topic ?? "UNKNOWN";

        _logger.LogInformation(
            "Notifica ricevuta | Topic={Topic} | Id={Id} | Attempt={Attempt}",
            topic, payload.Notification.NotificationId, payload.Notification.PublishAttemptCount);

        _ = RouteNotificationAsync(topic, payload);

        return new OkResult();
    }

    /// <summary>
    /// Smista la notifica in base al topic.
    /// Aggiungi i topic che registri nel Developer Portal eBay.
    /// </summary>
    private async Task RouteNotificationAsync(string topic, MarketplaceAccountDeletionPayload payload)
    {
        try
        {
            switch (topic)
            {
                case "MARKETPLACE_ACCOUNT_DELETION":
                    await HandleAccountDeletionAsync(payload.Notification!.Data);
                    break;

                // Aggiungi altri topic qui:
                // case "ITEM_SOLD":
                //     await HandleItemSoldAsync(payload.Notification!.Data); break;
                // case "FEEDBACK_LEFT":
                //     await HandleFeedbackAsync(payload.Notification!.Data); break;

                default:
                    _logger.LogWarning("Topic non gestito: {Topic}", topic);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante routing topic={Topic}", topic);
        }
    }

    private async Task HandleAccountDeletionAsync(AccountDeletionData? data)
    {
        if (data == null) return;

        _logger.LogInformation(
            "Account deletion via platform notification: UserId={UserId}",
            data.UserId);

        // TODO: stessa logica di MarketplaceAccountDeletionFunction
        await Task.CompletedTask;
    }
}