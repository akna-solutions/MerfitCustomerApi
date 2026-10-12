using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Customer.Notifications;

/// <summary>GET /api/notifications listesindeki tek bir bildirim.</summary>
public class CustomerNotificationDto
{
    public long Id { get; set; }

    /// <summary>NotificationType adi (orn. "WorkoutReminder", "NutritionReminder").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>"workout" | "nutrition" | "general".</summary>
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>GET /api/notifications sorgusu.</summary>
public class CustomerNotificationListRequest : PagedRequest
{
    /// <summary>Yalnizca bu kategorideki bildirimler: "workout" | "nutrition" | "general". Bos ise hepsi.</summary>
    public string? Category { get; set; }
}

public class CustomerNotificationUnreadCountResponse
{
    public int Count { get; set; }
}

/// <summary>GET /api/notifications/settings - backend'deki her NotificationType icin bir satir.</summary>
public class CustomerNotificationTypeSettingDto
{
    /// <summary>NotificationType adi.</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>"workout" | "nutrition" | "general".</summary>
    public string Category { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    /// <summary>true ise kullanici kapatamaz (orn. hesap/guvenlik ile ilgili sistem bildirimleri).</summary>
    public bool IsLocked { get; set; }
}

public class UpdateNotificationTypeSettingItem
{
    [Required]
    public string Type { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}

/// <summary>PUT /api/notifications/settings govdesi; yalnizca gonderilen turler guncellenir.</summary>
public class UpdateNotificationTypeSettingsRequest
{
    [Required]
    [MinLength(1)]
    public List<UpdateNotificationTypeSettingItem> Settings { get; set; } = new();
}
