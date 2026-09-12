using MerfitCustomerApi.Business.Dtos.Customer.Profile;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp'in ProfileContext'inin GET/PUT ihtiyacini karsilayan servis sozlesmesi.</summary>
public interface IProfileService
{
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa firlatilir.</exception>
    Task<CustomerProfileResponse> GetProfileAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yalnizca gonderilen (null olmayan) alanlari gunceller; guncellenmis tam profili dondurur.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ConflictException">Email veya kullanici adi baskasina aitse firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Gonderilen enum/tarih degerleri gecersizse firlatilir.</exception>
    Task<CustomerProfileResponse> UpdateProfileAsync(long userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}
