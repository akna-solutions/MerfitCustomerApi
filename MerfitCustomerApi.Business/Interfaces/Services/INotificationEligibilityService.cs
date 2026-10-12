using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Kullanicinin o gun programinda olan ve henuz tamamlanmamis antrenmani.</summary>
public sealed record ScheduledWorkout(long WorkoutPlanDayId, string WorkoutName);

/// <summary>
/// Hatirlatma kurallarinin is kurallari (ogun ve antrenman verisinin sahibi bu API'dir). Hem planlama
/// sirasinda hem de gonderimden hemen once ayni metotlar kullanilir.
/// </summary>
public interface INotificationEligibilityService
{
    /// <summary>
    /// Verilen yerel gunde bu ogunu beklenen ama henuz kaydetmemis kullanicilar. Beklenen ogun: aktif
    /// beslenme planinda o gun bu ogun varsa beklenir; aktif plani olmayan kullanici icin yalnizca
    /// onlyUsersWithPlan=false ise beklenir. Kayit: o tarih ve ogun turunde en az bir besin girilmis olmasi.
    /// </summary>
    Task<HashSet<long>> GetUsersMissingMealAsync(IReadOnlyCollection<long> userIds, DateOnly localDate, MealType mealType, bool onlyUsersWithPlan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktif antrenman planinda yerel gunun haftanin gunu icin antrenmani olan (dinlenme gunu degil) ve
    /// [dayStartUtc, dayEndUtc) araliginda tamamlanmis bir antrenman oturumu bulunmayan kullanicilar.
    /// </summary>
    Task<Dictionary<long, ScheduledWorkout>> GetUsersWithIncompleteWorkoutAsync(IReadOnlyCollection<long> userIds, DateOnly localDate, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default);
}
