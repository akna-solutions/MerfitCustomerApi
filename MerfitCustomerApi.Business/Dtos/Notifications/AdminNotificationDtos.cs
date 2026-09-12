using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Notifications;

public class AdminNotificationListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminNotificationDetailDto : AdminNotificationListItemDto
{
    public string? DataJson { get; set; }
}

public class AdminNotificationListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public bool? IsRead { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>POST /api/admin/notifications/user govdesi - tek bir kullaniciya bildirim gonderir.</summary>
public class AdminSendUserNotificationRequest
{
    [Required(ErrorMessage = "UserId zorunludur.")]
    public long UserId { get; set; }

    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icerik zorunludur.")]
    [StringLength(2000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    [StringLength(1000)]
    [Url]
    public string? ImageUrl { get; set; }

    /// <summary>Mobil uygulamanin bildirime tiklandiginda kullanacagi ek veri (JSON string).</summary>
    public string? DataJson { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>POST /api/admin/notifications/broadcast govdesi - TUM kullanicilara bildirim gonderir.</summary>
public class AdminBroadcastNotificationRequest
{
    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icerik zorunludur.")]
    [StringLength(2000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    [StringLength(1000)]
    [Url]
    public string? ImageUrl { get; set; }

    public string? DataJson { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Desteklenen kullanici segmentleri (madde 20).</summary>
public enum AdminNotificationSegment
{
    AllUsers,
    PlusUsers,
    FreeUsers,
    InactiveUsers,
    NewUsers,
    WorkoutInactiveUsers,
}

/// <summary>POST /api/admin/notifications/segment govdesi - belirli bir kullanici segmentine bildirim gonderir.</summary>
public class AdminSegmentNotificationRequest
{
    [Required(ErrorMessage = "Segment zorunludur.")]
    public AdminNotificationSegment Segment { get; set; }

    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icerik zorunludur.")]
    [StringLength(2000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    [StringLength(1000)]
    [Url]
    public string? ImageUrl { get; set; }

    public string? DataJson { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Toplu gonderim uc noktalarinin (broadcast/segment) ortak yaniti.</summary>
public class AdminNotificationSendResultDto
{
    public int RecipientCount { get; set; }
}
