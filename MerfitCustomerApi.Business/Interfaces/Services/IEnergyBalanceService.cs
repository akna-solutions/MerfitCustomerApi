using MerfitCustomerApi.Business.Dtos.Customer.Energy;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Gunluk kalori dengesi (alinan - harcanan). Yeni tablo kullanmaz: Meal/MealItem, DailyActivity (adim sayar),
/// WorkoutSession, UserProfile, BodyMeasurement ve NutritionGoal kayitlarini okur; hicbir sey yazmaz. Boylece besin,
/// adim veya antrenman kaydi degistiginde (gecmis tarihli olsa bile) sonraki istek guncel sonucu dondurur.
/// </summary>
public interface IEnergyBalanceService
{
    /// <summary>Tek gun; date verilmezse kullanicinin yerel bugunu.</summary>
    Task<EnergyDayDto> GetDayAsync(long userId, DateOnly? date, CancellationToken cancellationToken = default);

    /// <summary>Tarih araligi (en fazla EnergyBalanceOptions.MaxRangeDays gun), gunluk seri + ozet + haftalik ortalamalar.</summary>
    Task<EnergyRangeResponse> GetRangeAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
