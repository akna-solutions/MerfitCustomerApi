using MerfitCustomerApi.Business.Dtos.Customer.WorkoutPlans;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin (PersonalizationJob tarafindan onceden uretilip veritabanina kaydedilmis)
/// aktif kisisel antrenman programini SADECE OKUYAN servis sozlesmesi. Program burada
/// UYETILMEZ - bu servis DB'deki mevcut WorkoutPlan/WorkoutPlanDay/WorkoutPlanExercise
/// kayitlarini musteri DTO'larina esler (bkz. IWorkoutPlanGenerator, uretim sorumlulugu).
/// </summary>
public interface IMyPlanService
{
    /// <summary>Kullanicinin aktif kisisel antrenman programini dondurur; hic aktif plani yoksa null doner (404 firlatilmaz).</summary>
    Task<CustomerWorkoutPlanDto?> GetActivePlanAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bugunun (System.DayOfWeek, UTC) aktif plandaki karsiligini dondurur. Aktif plan yoksa
    /// veya bugun icin plana atanmis bir antrenman yoksa, ilgili flag'ler false olarak doner.
    /// </summary>
    Task<CustomerTodayWorkoutPlanResponse> GetTodayAsync(long userId, CancellationToken cancellationToken = default);
}
