using MerfitCustomerApi.Business.Dtos.Customer.Dashboard;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp DashboardScreen icin gereken tum verileri tek noktadan derleyen servis sozlesmesi.</summary>
public interface IDashboardService
{
    /// <summary>
    /// Verilen kullanici icin dashboard verisini hesaplar.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa firlatilir.</exception>
    Task<CustomerDashboardResponse> GetDashboardAsync(long userId, CancellationToken cancellationToken = default);
}
