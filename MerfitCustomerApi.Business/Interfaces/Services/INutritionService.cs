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

    /// <summary>Kullanicinin gunlugundeki bir kaydin duzenleme detayini dondurur.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kayit yoksa ya da baska kullaniciya aitse firlatilir.</exception>
    Task<CustomerMealItemDetailDto> GetMealItemAsync(long userId, long mealItemId, CancellationToken cancellationToken = default);

    /// <summary>Kaydin miktarini degistirir; besin degerleri kayit anindaki porsiyon degerleriyle yeniden hesaplanir.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kayit yoksa ya da baska kullaniciya aitse firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Miktar gecersizse firlatilir.</exception>
    Task<CustomerMealEntryDto> UpdateMealItemAsync(long userId, long mealItemId, UpdateMealItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Kaydi gunlukten siler.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kayit yoksa ya da baska kullaniciya aitse firlatilir.</exception>
    Task DeleteMealItemAsync(long userId, long mealItemId, CancellationToken cancellationToken = default);

    /// <summary>Verilen tarih icin su tuketimi kaydi ekler ve guncel toplami dondurur.</summary>
    Task<CustomerWaterDto> LogWaterAsync(long userId, LogWaterRequest request, CancellationToken cancellationToken = default);
}
