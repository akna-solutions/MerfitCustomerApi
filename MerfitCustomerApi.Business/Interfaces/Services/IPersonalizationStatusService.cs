using MerfitCustomerApi.Business.Dtos.Customer.Personalization;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Kullanicinin PersonalizationJob durumunu okuyan servis sozlesmesi.</summary>
public interface IPersonalizationStatusService
{
    /// <summary>
    /// Kullanicinin en son PersonalizationJob kaydinin durumunu dondurur. Kullanicinin hic
    /// job kaydi yoksa (beklenmedik, savunma amacli) HasJob=false ile doner.
    /// </summary>
    Task<PersonalizationStatusDto> GetStatusAsync(long userId, CancellationToken cancellationToken = default);
}
