using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// MerfitNativeApp'in aktif antrenman akisini (baslat/set logla/tamamla/iptal et) yoneten servis sozlesmesi.
/// Tum metotlar, oturumun cagiran kullaniciya ait oldugunu dogrular (IDOR korumasi).
/// </summary>
public interface IWorkoutSessionService
{
    /// <summary>
    /// Secilen antrenman icin yeni bir oturum baslatir; oturumun egzersiz/set sablonunu
    /// (WorkoutExercise plani + kullanicinin onceki rekorlari) hazirlar.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Workout bulunamaz veya pasifse firlatilir.</exception>
    Task<CustomerWorkoutSessionDto> StartSessionAsync(long userId, StartWorkoutSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devam eden (veya gecmis) bir oturumun guncel durumunu dondurur; ekran yeniden acildiginda
    /// state'i sunucudan senkronize etmek icin kullanilir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Oturum bulunamaz veya baska kullaniciya aitse firlatilir.</exception>
    Task<CustomerWorkoutSessionDto> GetSessionAsync(long userId, long sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir egzersiz-set sonucunu kaydeder (upsert: ayni SetNumber tekrar gonderilirse guncellenir).
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Oturum/egzersiz bulunamaz veya baska kullaniciya aitse firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Oturum zaten tamamlanmis/iptal edilmisse firlatilir.</exception>
    Task<CustomerWorkoutSessionExerciseDto> LogSetAsync(long userId, long sessionId, long sessionExerciseId, LogWorkoutSetRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Oturumu tamamlanmis olarak isaretler; seri (streak) guncellemesi ve yeni kisisel rekor
    /// tespiti bu adimda yapilir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Oturum bulunamaz veya baska kullaniciya aitse firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Oturum zaten tamamlanmis/iptal edilmisse firlatilir.</exception>
    Task<CustomerWorkoutSessionSummaryDto> CompleteSessionAsync(long userId, long sessionId, CompleteWorkoutSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Oturumu iptal edilmis (yarim birakildi) olarak isaretler.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Oturum bulunamaz veya baska kullaniciya aitse firlatilir.</exception>
    Task CancelSessionAsync(long userId, long sessionId, CancellationToken cancellationToken = default);
}
