using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Notifications;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Kullanicinin bildirimleri ve bildirim turu ayarlari.</summary>
public interface INotificationService
{
    Task<PagedResult<CustomerNotificationDto>> GetNotificationsAsync(long userId, CustomerNotificationListRequest request, CancellationToken cancellationToken = default);

    Task<CustomerNotificationUnreadCountResponse> GetUnreadCountAsync(long userId, string? category, CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default);

    Task MarkAllAsReadAsync(long userId, string? category, CancellationToken cancellationToken = default);

    /// <summary>app: "mbfit" | "mbshop" (bos ise kategoriye gore, varsayilan MB Fit).</summary>
    Task<CustomerNotificationUnreadCountResponse> GetUnreadCountAsync(long userId, string? app, string? category, CancellationToken cancellationToken = default);

    Task MarkAllAsReadAsync(long userId, string? app, string? category, CancellationToken cancellationToken = default);

    Task<List<CustomerNotificationTypeSettingDto>> GetSettingsAsync(long userId, CancellationToken cancellationToken = default);

    Task<List<CustomerNotificationTypeSettingDto>> UpdateSettingsAsync(long userId, UpdateNotificationTypeSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Push genel anahtari, sessiz saatler ve saat dilimi.</summary>
    Task<CustomerNotificationPreferencesDto> GetPreferencesAsync(long userId, CancellationToken cancellationToken = default);

    Task<CustomerNotificationPreferencesDto> UpdatePreferencesAsync(long userId, UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Gondericinin bir kullanicinin bildirim tercihlerini topluca okumasi icin.</summary>
public sealed record UserNotificationPreferences(
    IReadOnlySet<MerfitCustomerApi.Domain.Entities.Enums.NotificationType> DisabledTypes,
    bool PushEnabled,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string? TimeZone);

/// <summary>Tur bazli tercihler, push anahtari, sessiz saat ve saat dilimi; kaynak NotificationService katalogudur.</summary>
public interface INotificationPreferenceReader
{
    /// <summary>Verilen her kullanici icin tercihler (kaydi olmayanlar icin varsayilanlar).</summary>
    Task<IReadOnlyDictionary<long, UserNotificationPreferences>> GetPreferencesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);
}
