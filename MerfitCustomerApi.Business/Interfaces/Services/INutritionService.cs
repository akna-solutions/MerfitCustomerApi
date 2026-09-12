using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp NutritionScreen'in gunluk beslenme verisini ve loglama akislarini yoneten servis sozlesmesi.</summary>
public interface INutritionService
{
    /// <summary>
    /// Verilen tarih icin kullanicinin gunluk beslenme ozetini dondurur. NutritionGoal kaydi yoksa
    /// profil verilerinden (BMR/TDEE) hesaplanip kalici olarak olusturulur.
    /// </summary>
    Task<CustomerNutritionResponse> GetDailyNutritionAsync(long userId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir besini (Food) verilen ogun/tarih icin kullanicinin gunlugune ekler.
    /// Gun+ogun turu icin Meal kaydi yoksa olusturulur, ardindan bir MealItem eklenir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Food bulunamazsa firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">MealType degeri gecersizse firlatilir.</exception>
    Task<CustomerMealEntryDto> LogMealItemAsync(long userId, LogMealItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Verilen tarih icin su tuketimi kaydi ekler ve guncel toplami dondurur.</summary>
    Task<CustomerWaterDto> LogWaterAsync(long userId, LogWaterRequest request, CancellationToken cancellationToken = default);
}
