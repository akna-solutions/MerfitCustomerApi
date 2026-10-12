using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Admin panelinden olusturulan tek seferlik veya zamanlanmis duyuru. Alici listesi ScheduledAtUtc
/// geldiginde MerfitApi'deki kampanya calisani tarafindan NotificationDispatch satirlarina donusturulur.
/// </summary>
public class NotificationCampaign : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public NotificationCampaignStatus Status { get; set; } = NotificationCampaignStatus.Draft;

    public NotificationAudienceSegment Segment { get; set; }

    public long? NotificationTemplateId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? DataJson { get; set; }

    public DateTime? ScheduledAtUtc { get; set; }

    /// <summary>Uygulama ici bildirimin gosterilecegi son tarih (opsiyonel).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Alici listesi olusturuldugunda kuyruga yazilan alici sayisi.</summary>
    public int? RecipientCount { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public Guid? LockToken { get; set; }

    public DateTime? LockedUntilUtc { get; set; }
}

/// <summary>SingleUser/SelectedUsers kampanyalarinin hedef kullanicilari.</summary>
public class NotificationCampaignUser : BaseEntity
{
    public long NotificationCampaignId { get; set; }

    public long UserId { get; set; }
}
