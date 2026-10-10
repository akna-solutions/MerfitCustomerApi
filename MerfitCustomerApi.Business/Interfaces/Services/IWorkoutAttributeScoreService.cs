using MerfitCustomerApi.Business.Dtos.Admin.Workouts;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Antrenman kataloğundaki ozellik puanlarinin (WorkoutAttributeScore) admin tarafindan
/// sorgulanmasi ve guncellenmesi icin servis sozlesmesi.
/// </summary>
public interface IWorkoutAttributeScoreService
{
    /// <exception cref="Domain.Exceptions.NotFoundException">Antrenman bulunamazsa firlatilir.</exception>
    Task<AdminWorkoutAttributeScoresDto> GetAsync(long workoutId, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.NotFoundException">Antrenman bulunamazsa firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Puan 0-100 disindaysa, tur gecersizse veya tekrarlaniyorsa firlatilir.</exception>
    Task<AdminWorkoutAttributeScoresDto> SetAsync(
        long workoutId,
        AdminSetWorkoutAttributeScoresRequest request,
        long? adminUserId,
        CancellationToken cancellationToken = default);
}
