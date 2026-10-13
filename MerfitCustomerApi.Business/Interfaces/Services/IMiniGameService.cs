using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Mini oyun (Dumbbell Dash) oturumlari ve skora bagli MB FIT puani. Puan ayri bir cuzdana degil, mevcut
/// puan motoruna (MerfitScoreService, "mini_game" kategorisi) MiniGameSession satiri uzerinden yansir.
/// </summary>
public interface IMiniGameService
{
    /// <summary>Yeni oyun oturumu acar (varsa kullanicinin bitirilmemis oturumu terk edilmis sayilir).</summary>
    Task<MiniGameSessionDto> StartSessionAsync(long userId, StartMiniGameSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sonucu sunucuda yeniden oynatarak dogrular ve odulu verir. Idempotenttir: ayni oturum icin tekrar
    /// cagrilirsa ilk sonucu (ikinci kez puan vermeden) dondurur.
    /// </summary>
    Task<MiniGameResultDto> FinishSessionAsync(long userId, long sessionId, FinishMiniGameSessionRequest request, CancellationToken cancellationToken = default);
}
