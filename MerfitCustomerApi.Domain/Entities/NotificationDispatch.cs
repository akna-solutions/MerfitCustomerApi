using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Gonderim kuyrugu (outbox): bir kullaniciya gidecek tek bir bildirim. Kurallar, kampanyalar ve
/// aylik odul isleri yalnizca bu tabloya yazar; tek gonderici (MerfitCustomerApi
/// NotificationDispatchProcessor) uygunlugu gonderimden hemen once yeniden degerlendirir, uygulama
/// ici bildirimi (Notification) olusturur ve push saglayicisina iletir.
/// DedupeKey benzersizdir; ayni is tekrar calissa bile ayni bildirim ikinci kez olusmaz.
/// </summary>
public class NotificationDispatch : BaseEntity
{
    public long UserId { get; set; }

    public NotificationType Type { get; set; }

    public NotificationDispatchSource Source { get; set; }

    public long? NotificationRuleId { get; set; }

    public long? NotificationCampaignId { get; set; }

    public long? NotificationTemplateId { get; set; }

    public long? LeaderboardPeriodId { get; set; }

    public string DedupeKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? DataJson { get; set; }

    /// <summary>Kural gonderimlerinde kullanicinin yerel tarihi (kural kosulu bu gune gore degerlendirilir).</summary>
    public DateOnly? LocalDate { get; set; }

    public DateTime ScheduledForUtc { get; set; }

    /// <summary>Bu andan sonra gonderilmez (bayat hatirlatma onlenir).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public NotificationDispatchStatus Status { get; set; } = NotificationDispatchStatus.Pending;

    /// <summary>Suppressed/Cancelled/InAppOnly nedenini aciklayan kisa kod.</summary>
    public string? StatusReason { get; set; }

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public Guid? LockToken { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Olusturulan uygulama ici bildirim; yeniden denemelerde ikinci kez olusturulmaz.</summary>
    public long? NotificationId { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Bir gonderimin bir cihaza yapilan saglayici denemesi ve saglayici yaniti.</summary>
public class NotificationDeliveryAttempt : BaseEntity
{
    public long NotificationDispatchId { get; set; }

    public long UserDeviceId { get; set; }

    public int AttemptNumber { get; set; }

    public NotificationDeliveryStatus Status { get; set; }

    /// <summary>Saglayicinin mesaj/bilet kimligi (Expo ticket id).</summary>
    public string? ProviderMessageId { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? ReceiptCheckedAtUtc { get; set; }
}
