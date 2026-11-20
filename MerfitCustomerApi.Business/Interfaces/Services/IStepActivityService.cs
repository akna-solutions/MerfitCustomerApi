using MerfitCustomerApi.Business.Dtos.Customer.Activity;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Adim sayar: gunluk aktivite senkronizasyonu (idempotent), hedef ve istatistikler. Tum metotlar yalnizca
/// verilen kullanicinin (JWT) kayitlarina erisir.
/// </summary>
public interface IStepActivityService
{
    /// <summary>
    /// Gunleri saat bazinda "buyuk olan kalir" kuraliyla birlestirir; ayni istek tekrar gelirse hicbir sey
    /// degismez. Makullik sinirlarina takilan gun isaretlenir ve basari/puana katilmaz.
    /// </summary>
    Task<StepSyncResponse> SyncAsync(long userId, StepSyncRequest request, CancellationToken cancellationToken = default);

    Task<StepSettingsDto> GetSettingsAsync(long userId, CancellationToken cancellationToken = default);

    Task<StepSettingsDto> UpdateGoalAsync(long userId, UpdateStepGoalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Tek gun detayi (saatlik adimlar dahil); kayit yoksa sifirlarla doner.</summary>
    Task<StepDayDto> GetDayAsync(long userId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Tarih araligindaki her gun (kayit olmayan gunler sifirla).</summary>
    Task<StepRangeResponse> GetRangeAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>EndDate dahil son 7 gun ozeti.</summary>
    Task<StepWeeklySummaryDto> GetWeeklySummaryAsync(long userId, DateOnly endDate, CancellationToken cancellationToken = default);
}
