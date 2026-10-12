using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// MB FIT puan motoru. Kullanicinin uygulamada yaptigi aksiyonlardan (antrenman tamamlama, kisisel
/// rekor, ogun/su kaydi, basari) puan olaylarini turetir, donem bazli siralar ve kullanicinin
/// MerfitScore/MerfitScoreHistory ozetini gunceller.
/// </summary>
public interface IMerfitScoreService
{
    /// <summary>[fromUtc, toUtc) araligindaki puan olaylari; null sinir = sinirsiz. userId verilirse yalnizca o kullanici.</summary>
    Task<IReadOnlyList<MerfitPointEvent>> GetPointEventsAsync(DateTime? fromUtc, DateTime? toUtc, long? userId = null, CancellationToken cancellationToken = default);

    /// <summary>Araliktaki puanlara gore (puan > 0) siralanmis kullanicilar.</summary>
    Task<List<MerfitRankedUser>> RankUsersAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Donem turune gore siralama araligini ve odul donemini cozer. Bugunu kapsayan aktif bir
    /// LeaderboardPeriod varsa onun tarihleri, yoksa takvim haftasi/ayi kullanilir. AllTime sinirsizdir.
    /// </summary>
    Task<MerfitScoreWindow> ResolveWindowAsync(LeaderboardPeriodType periodType, CancellationToken cancellationToken = default);

    /// <summary>Yerel saate gore bugunun baslangici (UTC).</summary>
    DateTime GetLocalDayStartUtc(DateTime utc);

    /// <summary>
    /// Kosulu saglanan basarilari kullaniciya verir ve MerfitScore (AllTime) / MerfitScoreBreakdown /
    /// MerfitScoreHistory ozetini gunceller. Hata olursa loglar ve yutar; cagiran aksiyon bozulmaz.
    /// </summary>
    Task TryRefreshUserAsync(long userId, CancellationToken cancellationToken = default);
}

public record MerfitPointEvent(long UserId, DateTime OccurredAtUtc, string Category, int Points);

public record MerfitRankedUser(long UserId, int Rank, int Points, int Workouts);

public record MerfitScoreWindow(DateTime? FromUtc, DateTime? ToUtc, LeaderboardPeriod? Period);
