using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Customer.Notifications;

/// <summary>GET /api/notifications listesindeki tek bir bildirim.</summary>
public class CustomerNotificationDto
{
    public long Id { get; set; }

    /// <summary>NotificationType adi (orn. "WorkoutReminder", "NutritionReminder").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>"workout" | "nutrition" | "general" | "shop".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>"mbfit" | "mbshop": bildirimin gosterildigi gelen kutusu.</summary>
    public string App { get; set; } = "mbfit";

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Dokunuldugunda acilacak ekran (orn. { "screen": "nutrition" }); bos olabilir.</summary>
    public Dictionary<string, string> Data { get; set; } = new();
}

/// <summary>GET /api/notifications sorgusu.</summary>
public class CustomerNotificationListRequest : PagedRequest
{
    /// <summary>"mbfit" (varsayilan) | "mbshop". MB Fit ve MB Shop gelen kutulari ayridir.</summary>
    public string? App { get; set; }

    /// <summary>Yalnizca bu kategorideki bildirimler: "workout" | "nutrition" | "general" | "shop". Bos ise uygulamanin tumu.</summary>
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

    /// <summary>"workout" | "nutrition" | "general" | "shop".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>"mbfit" | "mbshop": mobil ayarlar ekraninda hangi bolumde gosterilecegi.</summary>
    public string App { get; set; } = "mbfit";

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

/// <summary>GET/PUT /api/notifications/preferences.</summary>
public class CustomerNotificationPreferencesDto
{
    public bool PushEnabled { get; set; }

    /// <summary>"SS:dd" (kullanicinin yerel saati) veya null.</summary>
    public string? QuietHoursStart { get; set; }

    public string? QuietHoursEnd { get; set; }

    /// <summary>Kullanicinin kayitli IANA saat dilimi (null ise varsayilan kullanilir).</summary>
    public string? TimeZone { get; set; }

    /// <summary>Bildirim zamanlamasinda fiilen kullanilan saat dilimi.</summary>
    public string EffectiveTimeZone { get; set; } = string.Empty;
}

public class UpdateNotificationPreferencesRequest
{
    public bool PushEnabled { get; set; } = true;

    [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Saat SS:dd biçiminde olmalı.")]
    public string? QuietHoursStart { get; set; }

    [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Saat SS:dd biçiminde olmalı.")]
    public string? QuietHoursEnd { get; set; }

    /// <summary>Verilmezse mevcut saat dilimi korunur.</summary>
    [StringLength(100)]
    public string? TimeZone { get; set; }
}

/// <summary>PUT /api/notifications/devices govdesi. Kullanici kimligi her zaman JWT'den alinir.</summary>
public class RegisterDeviceRequest
{
    [Required]
    [StringLength(500)]
    public string Token { get; set; } = string.Empty;

    /// <summary>"ios" | "android".</summary>
    [Required]
    [StringLength(20)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>Uygulamanin cihazda sakladigi kalici kurulum kimligi.</summary>
    [StringLength(100)]
    public string? InstallationId { get; set; }

    [StringLength(200)]
    public string? DeviceName { get; set; }

    [StringLength(50)]
    public string? AppVersion { get; set; }

    [StringLength(50)]
    public string? OsVersion { get; set; }

    /// <summary>Cihazin IANA saat dilimi; gecerliyse kullanicinin saat dilimi olarak kaydedilir.</summary>
    [StringLength(100)]
    public string? TimeZone { get; set; }
}

/// <summary>POST /api/notifications/devices/deactivate govdesi.</summary>
public class DeactivateDeviceRequest
{
    [Required]
    [StringLength(500)]
    public string Token { get; set; } = string.Empty;

    /// <summary>"Logout" | "PermissionRevoked". Diger degerler "Logout" sayilir.</summary>
    [StringLength(50)]
    public string? Reason { get; set; }
}

public class CustomerDeviceRegistrationDto
{
    public long Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
