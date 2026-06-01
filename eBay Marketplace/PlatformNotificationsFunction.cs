using eBay_Marketplace.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace eBay_Marketplace.Functions;

/// <summary>
/// Azure Function che gestisce le Platform Notifications (push) di eBay.
/// Supporta eventi come ITEM_SOLD, ITEM_LISTED, FEEDBACK_LEFT, ecc.
///
/// Docs: https://developer.ebay.com/devzone/xml/docs/reference/ebay/types/NotificationEventTypeCodeType.html
/// </summary>
public class PlatformNotificationsFunction
{
    private readonly ILogger<PlatformNotificationsFunction> _logger;

    private static readonly string VerificationToken = Environment.GetEnvironmentVariable("EBAY_VERIFICATION_TOKEN") ?? string.Empty;

    private static readonly string EndpointUrl =  Environment.GetEnvironmentVariable("EBAY_ENDPOINT_URL") ?? string.Empty;

    public PlatformNotificationsFunction(ILogger<PlatformNotificationsFunction> logger)
    {
        _logger = logger;
    }

    [Function("PlatformNotifications")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "ebay/notifications")] HttpRequest req)
    {
        if (req.Method == HttpMethods.Get)
            return HandleChallengeVerification(req);        

        return await HandlePlatformNotification(req);
    }

    private IActionResult HandleChallengeVerification(HttpRequest req)
    {
        var challengeCode = req.Query["challenge_code"].ToString();
        if (string.IsNullOrEmpty(challengeCode))
            return new BadRequestObjectResult("challenge_code mancante");

        var rawInput = $"{challengeCode}{VerificationToken}{EndpointUrl}";
        var hashBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _logger.LogInformation("Challenge verification completata: {Code}", challengeCode);
        return new OkObjectResult(new ChallengeResponseDto { ChallengeResponse = hashHex });
    }

    private async Task<IActionResult> HandlePlatformNotification(HttpRequest req)
    {
        string body;
        try
        {
            using var reader = new StreamReader(req.Body);
            body = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore lettura body");
            return new BadRequestResult();
        }

        EbayNotificationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EbayNotificationPayload>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON non valido");
            return new BadRequestObjectResult("Payload JSON non valido");
        }

        if (payload?.Notification == null)
        {
            _logger.LogWarning("Payload privo di notifica");
            return new BadRequestObjectResult("Payload incompleto");
        }

        var notif = payload.Notification;
        _logger.LogInformation(
            "Notifica ricevuta | Id={Id} | EventDate={Date}",
            notif.NotificationId, notif.EventDate);

        // Smista per topic / evento
        await RouteNotificationAsync(notif);

        return new OkResult();
    }

    /// <summary>
    /// Smista la notifica in base al topic ricevuto.
    /// Estendi con i topic che registri nel Developer Portal.
    /// </summary>
    private async Task RouteNotificationAsync(EbayNotificationData notif)
    {
        // Il topic arriva nel campo Metadata del payload padre oppure
        // si può deserializzare il body raw per estrarlo.
        // Qui usiamo un campo generico "Topic" come esempio.

        _logger.LogInformation("Routing notifica Id={Id}", notif.NotificationId);

        // TODO: aggiungi uno switch sul topic reale
        // switch (topic)
        // {
        //     case "MARKETPLACE_ACCOUNT_DELETION":
        //         await HandleAccountDeletion(notif); break;
        //     case "ITEM_SOLD":
        //         await HandleItemSold(notif); break;
        //     default:
        //         _logger.LogWarning("Topic non gestito: {Topic}", topic); break;
        // }

        await Task.CompletedTask;
    }
}