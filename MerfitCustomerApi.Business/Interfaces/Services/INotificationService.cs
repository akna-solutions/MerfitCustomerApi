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

    Task<List<CustomerNotificationTypeSettingDto>> GetSettingsAsync(long userId, CancellationToken cancellationToken = default);

    Task<List<CustomerNotificationTypeSettingDto>> UpdateSettingsAsync(long userId, UpdateNotificationTypeSettingsRequest request, CancellationToken cancellationToken = default);
}
