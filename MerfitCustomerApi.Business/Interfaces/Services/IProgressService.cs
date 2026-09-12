using MerfitCustomerApi.Business.Dtos.Customer.Progress;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp ProgressScreen'in gerektirdigi tum ilerleme verilerini derleyen servis sozlesmesi.</summary>
public interface IProgressService
{
    /// <summary>
    /// Verilen zaman araligina (kilo grafigi icin) gore kullanicinin ilerleme ozetini dondurur.
    /// </summary>
    /// <param name="range">"week" | "month" | "3months" | "year".</param>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa firlatilir.</exception>
    Task<CustomerProgressResponse> GetProgressAsync(long userId, string range, CancellationToken cancellationToken = default);
}
