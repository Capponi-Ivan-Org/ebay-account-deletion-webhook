using System.Text.Json.Serialization;

namespace eBay_Marketplace.Models;

public class MarketplaceAccountDeletionPayload
{
    [JsonPropertyName("metadata")]
    public NotificationMetadata? Metadata { get; set; }

    [JsonPropertyName("notification")]
    public NotificationEnvelope? Notification { get; set; }
}

public class NotificationMetadata
{
    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; set; }
}

public class NotificationEnvelope
{
    [JsonPropertyName("notificationId")]
    public string? NotificationId { get; set; }

    [JsonPropertyName("eventDate")]
    public string? EventDate { get; set; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; set; }

    [JsonPropertyName("publishAttemptCount")]
    public int PublishAttemptCount { get; set; }

    [JsonPropertyName("data")]
    public AccountDeletionData? Data { get; set; }
}

public class AccountDeletionData
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("eiasToken")]
    public string? EiasToken { get; set; }
}

// ── Risposta al challenge GET ────────────────────────────────────────────────
// NOTA: il campo JSON deve chiamarsi "challengeResponse" (camelCase)
// Non usare il nome della classe come nome della proprietà per evitare
// ambiguità di serializzazione.
public class ChallengeVerificationResponse
{
    [JsonPropertyName("challengeResponse")]
    public string? Value { get; set; }
}