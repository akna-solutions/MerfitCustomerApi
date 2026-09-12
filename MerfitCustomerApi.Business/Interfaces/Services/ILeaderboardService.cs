using MerfitCustomerApi.Business.Dtos.Customer.Leaderboard;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp LeaderboardScreen'in gerektirdigi tum siralama/odul/basari verilerini derleyen servis sozlesmesi.</summary>
public interface ILeaderboardService
{
    /// <param name="period">"week" | "month" | "allTime".</param>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa firlatilir.</exception>
    Task<CustomerLeaderboardResponse> GetLeaderboardAsync(long userId, string period, CancellationToken cancellationToken = default);
}
