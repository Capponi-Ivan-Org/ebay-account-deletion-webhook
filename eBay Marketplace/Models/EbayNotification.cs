namespace eBay_Marketplace.Models;

/// <summary>
/// Payload ricevuto da eBay per le notifiche push (Platform Notifications).
/// </summary>
public class EbayNotificationPayload
{
    public string? Metadata { get; set; }
    public EbayNotificationData? Notification { get; set; }
}

public class EbayNotificationData
{
    public string? NotificationId { get; set; }
    public string? EventDate { get; set; }
    public string? PublishDate { get; set; }
    public string? PublishAttemptCount { get; set; }
    public EbayNotificationTopic? Data { get; set; }
}

public class EbayNotificationTopic
{
    public string? Username { get; set; }
    public string? UserId { get; set; }
    public string? EiasToken { get; set; }
}

/// <summary>
/// Payload ricevuto da eBay per la cancellazione account marketplace.
/// Vedi: https://developer.ebay.com/marketplace-account-deletion
/// </summary>
public class MarketplaceAccountDeletionPayload
{
    public string? Metadata { get; set; }
    public MarketplaceAccountDeletionData? Notification { get; set; }
}

public class MarketplaceAccountDeletionData
{
    public string? NotificationId { get; set; }
    public string? EventDate { get; set; }
    public string? PublishDate { get; set; }
    public string? PublishAttemptCount { get; set; }
    public MarketplaceAccountDeletionTopic? Data { get; set; }
}

public class MarketplaceAccountDeletionTopic
{
    public string? Username { get; set; }
    public string? UserId { get; set; }
    public string? EiasToken { get; set; }
}

/// <summary>
/// Risposta al challenge di verifica endpoint eBay.
/// </summary>
public class ChallengeResponseDto
{
    public string? ChallengeResponse { get; set; }
}