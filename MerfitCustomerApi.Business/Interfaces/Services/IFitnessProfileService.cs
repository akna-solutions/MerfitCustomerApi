using MerfitCustomerApi.Business.Dtos.Auth;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Yalnizca alisveris icin kayit olmus (fitness alanlari bos) kullanicinin MB Fit'i kullanmak istediginde
/// onboarding'in fitness adimlarini sonradan tamamlamasi.
/// </summary>
public interface IFitnessProfileService
{
    /// <summary>
    /// Fitness alanlarini kayittaki kurallarla profile yazar, hedef/beslenme hedefi/ilk kilo kaydini olusturur ve
    /// kisisel plan uretimini kuyruga alir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">Fitness profili zaten tamamlanmissa.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Boy veya kilo eksikse ya da olcumler tutarsizsa.</exception>
    Task CompleteAsync(long userId, FitnessProfileRequest request, CancellationToken cancellationToken = default);
}
