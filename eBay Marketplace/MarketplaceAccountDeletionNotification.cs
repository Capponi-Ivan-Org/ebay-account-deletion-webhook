using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using eBay_Marketplace.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace eBay_Marketplace;

/// <summary>
/// Azure Function che gestisce le notifiche di cancellazione account eBay Marketplace.
/// eBay richiede che l'endpoint:
/// 1. Risponda al challenge GET per la verifica iniziale dell'endpoint
/// 2. Gestisca i POST con il payload di cancellazione account
/// Docs: https://developer.ebay.com/marketplace-account-deletion
/// </summary>
public class MarketplaceAccountDeletionFunction
{
    private readonly ILogger<MarketplaceAccountDeletionFunction> _logger;

    // Recupera il verification token configurato nel Developer Portal eBay
    private static readonly string VerificationToken = Environment.GetEnvironmentVariable("EBAY_VERIFICATION_TOKEN") ?? string.Empty;

    // L'URL pubblico di questo endpoint (deve corrispondere a quello registrato su eBay)
    private static readonly string EndpointUrl = Environment.GetEnvironmentVariable("EBAY_ENDPOINT_URL") ?? string.Empty;

    public MarketplaceAccountDeletionFunction(ILogger<MarketplaceAccountDeletionFunction> logger) => _logger = logger;

    [Function("MarketplaceAccountDeletion")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "ebay/account-deletion")] HttpRequest req)
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
            _logger.LogWarning("GET ricevuto senza challenge_code");
            return new BadRequestObjectResult("challenge_code mancante");
        }

        if (string.IsNullOrEmpty(VerificationToken) || string.IsNullOrEmpty(EndpointUrl))
        {
            _logger.LogError("EBAY_VERIFICATION_TOKEN o EBAY_ENDPOINT_URL non configurati");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        var rawInput = $"{challengeCode}{VerificationToken}{EndpointUrl}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _logger.LogInformation("Challenge verification completata per code: {Code}", challengeCode);

        var response = new ChallengeResponseDto { ChallengeResponse = hashHex };
        return new OkObjectResult(response);
    }

    private async Task<IActionResult> HandleAccountDeletionNotification(HttpRequest req)
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

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Body POST vuoto");
            return new BadRequestObjectResult("Payload vuoto");
        }

        MarketplaceAccountDeletionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MarketplaceAccountDeletionPayload>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON non valido: {Body}", body);
            return new BadRequestObjectResult("Payload JSON non valido");
        }

        if (payload?.Notification?.Data == null)
        {
            _logger.LogWarning("Payload privo di dati notifica");
            return new BadRequestObjectResult("Payload incompleto");
        }

        var data = payload.Notification.Data;

        _logger.LogInformation(
            "Account deletion ricevuta | NotificationId={Id} | UserId={UserId} | Username={Username}",
            payload.Notification.NotificationId,
            data.UserId,
            data.Username);

        await ProcessAccountDeletionAsync(data.UserId, data.Username, data.EiasToken);
        return new OkResult();
    }

    /// <summary>
    /// Implementa qui la rimozione/anonimizzazione dei dati utente.
    /// GDPR/CCPA: i dati dell'utente devono essere cancellati entro 30 giorni.
    /// </summary>
    private async Task ProcessAccountDeletionAsync(string? userId, string? username, string? eiasToken)
    {
        // TODO: implementa la cancellazione reale nei tuoi store:
        //   - Database SQL / Cosmos DB
        //   - Blob Storage
        //   - Cache / Redis
        //   - Servizi downstream

        _logger.LogInformation(
            "Elaborazione cancellazione account: UserId={UserId}, EiasToken={Token}",
            userId, eiasToken);

        // Esempio: invio a una Service Bus queue per elaborazione asincrona
        // await _serviceBusSender.SendMessageAsync(new ServiceBusMessage(
        //     JsonSerializer.Serialize(new { UserId = userId, Username = username })));

        await Task.CompletedTask;
    }
}