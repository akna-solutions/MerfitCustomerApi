using MerfitCustomerApi.Business.Dtos.Customer.Calendar;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Ilerleme takvimi ve ana sayfa haftalik aktivite bolumu icin gunluk ozetler.</summary>
public interface IActivityCalendarService
{
    /// <summary>[from, to] (ikisi de dahil, kullanicinin yerel tarihleri) araligindaki her gunun ozeti.</summary>
    Task<CustomerActivityCalendarResponse> GetCalendarAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
