using MerfitCustomerApi.Business.Dtos.Customer.Notifications;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Giris yapmis kullanicinin push cihaz kayitlari. Kullanici kimligi her zaman JWT'den gelir.</summary>
public interface INotificationDeviceService
{
    Task<CustomerDeviceRegistrationDto> RegisterAsync(long userId, RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Yalnizca bu kullaniciya ait token'i pasiflestirir; baskasinin token'i icin sessizce hicbir sey yapmaz.</summary>
    Task DeactivateAsync(long userId, DeactivateDeviceRequest request, CancellationToken cancellationToken = default);
}
