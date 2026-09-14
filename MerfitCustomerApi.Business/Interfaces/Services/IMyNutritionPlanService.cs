using MerfitCustomerApi.Business.Dtos.Customer.NutritionPlans;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin (PersonalizationJob tarafindan onceden uretilip veritabanina kaydedilmis)
/// aktif kisisel beslenme programini SADECE OKUYAN servis sozlesmesi. Bu servis, gercekte
/// tuketilen ogunleri loglayan INutritionService'ten (Meal/MealItem) tamamen ayridir.
/// </summary>
public interface IMyNutritionPlanService
{
    /// <summary>Kullanicinin aktif kisisel beslenme programini dondurur; hic aktif plani yoksa null doner.</summary>
    Task<CustomerNutritionPlanDto?> GetActivePlanAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Bugunun (System.DayOfWeek, UTC) aktif plandaki karsiligini dondurur.</summary>
    Task<CustomerTodayNutritionPlanResponse> GetTodayAsync(long userId, CancellationToken cancellationToken = default);
}
