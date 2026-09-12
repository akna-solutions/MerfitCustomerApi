using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Workouts;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp Workouts ekraninin katalog (listeleme/filtreleme/detay) ihtiyaclarini karsilayan servis sozlesmesi.</summary>
public interface IWorkoutService
{
    /// <summary>Filtrelenmis, sayfalanmis antrenman listesini dondurur. "Personalized" true ise kullanicinin profiline gore filtrelenir.</summary>
    Task<PagedResult<CustomerWorkoutListItemDto>> GetWorkoutsAsync(long userId, CustomerWorkoutListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tek bir antrenmanin detayini (egzersiz listesiyle birlikte) dondurur.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Antrenman bulunamazsa firlatilir.</exception>
    Task<CustomerWorkoutDetailDto> GetWorkoutDetailAsync(long workoutId, CancellationToken cancellationToken = default);
}
