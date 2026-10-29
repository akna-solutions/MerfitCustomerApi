using MerfitCustomerApi.Business.Dtos.Customer.Achievements;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Kullanicinin basarilari ve yeni kazanim kutlamasinin bir kez gosterilmesi.</summary>
public interface IAchievementService
{
    /// <summary>Kosullari yeniden degerlendirip (yeni kazanimlari yazar) kullanicinin basari listesini dondurur.</summary>
    Task<CustomerAchievementsResponse> GetMyAchievementsAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Kutlamasi gosterilen kazanimlari isaretler; yalnizca kullanicinin kendi kayitlari etkilenir. Idempotenttir.</summary>
    Task MarkSeenAsync(long userId, MarkAchievementsSeenRequest request, CancellationToken cancellationToken = default);
}
