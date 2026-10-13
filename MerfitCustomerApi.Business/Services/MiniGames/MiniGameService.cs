using System.Security.Cryptography;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.MiniGames;

/// <summary>
/// IMiniGameService'in varsayilan implementasyonu.
///
/// Guvenlik modeli (ayrintisi: reports/mini-oyun/rapor.md):
/// - Kullanici kimligi yalnizca JWT'den gelir; oturum baska kullaniciya aitse 404 doner.
/// - Seed sunucuda uretilir; skor istemciden alinmaz, oyun seed + zipla tick'leriyle sunucuda yeniden
///   oynatilir (DumbbellDashSimulator). Iddia edilen skor/tick tutmazsa sonuc reddedilir.
/// - Simulasyon suresi, oturumun acilisindan beri gecen gercek sureyi asamaz (hizlandirilmis oyun reddedilir).
/// - Oturum basina tek odul: Status iyimser eszamanlilik belirteci, tekrar gonderim ilk sonucu dondurur.
/// - Kullanici basina tek aktif oturum (filtreli benzersiz index): gunluk limit eszamanli isteklerle asilamaz.
/// - Oyun basina, gunluk puan ve gunluk oturum limitleri MiniGameRewardOptions'tan gelir.
/// Sinir: Seed istemciye verilmek zorunda oldugu icin degistirilmis bir istemci kusursuz girdi uretebilir;
/// bu durumda bile kazanc gunluk limitle ve oyunun gercek suresiyle sinirlidir.
/// </summary>
public class MiniGameService : IMiniGameService
{
    public const string DumbbellDashGameKey = "dumbbell-dash";
    public const string MiniGameCategory = MerfitScoreService.MiniGameCategory;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;
    private readonly MiniGameRewardOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MiniGameService> _logger;

    public MiniGameService(
        IUnitOfWork unitOfWork,
        IMerfitScoreService scoreService,
        IOptions<MiniGameRewardOptions> options,
        TimeProvider timeProvider,
        ILogger<MiniGameService> logger)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MiniGameSessionDto> StartSessionAsync(long userId, StartMiniGameSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EngineVersion != DumbbellDashSimulator.EngineVersion)
        {
            throw new AppValidationException(nameof(request.EngineVersion), "Mini oyun sürümü güncel değil. Puan kazanmak için uygulamayı güncelle.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var dayStart = _scoreService.GetLocalDayStartUtc(now);
        var earnedToday = await GetEarnedTodayAsync(userId, dayStart, cancellationToken);

        if (!_options.Enabled)
        {
            return new MiniGameSessionDto
            {
                RewardsEnabled = false,
                EngineVersion = DumbbellDashSimulator.EngineVersion,
                Rules = BuildRules(),
                EarnedToday = earnedToday,
                RemainingToday = 0,
            };
        }

        var repo = _unitOfWork.Repository<MiniGameSession>();
        var startedToday = await repo.GetQueryable()
            .CountAsync(s => s.UserId == userId && s.StartedAt >= dayStart, cancellationToken);
        if (startedToday >= _options.MaxSessionsPerDay)
        {
            throw new TooManyRequestsException("Bugün için oyun sınırına ulaştın. Yarın tekrar oynayabilirsin.");
        }

        // Bitirilmemis onceki oturum terk edilmis sayilir; boylece ayni anda tek oturum odul alabilir.
        var active = await repo.GetQueryable(asNoTracking: false)
            .Where(s => s.UserId == userId && s.Status == MiniGameSessionStatus.Started)
            .ToListAsync(cancellationToken);
        foreach (var previous in active)
        {
            previous.Status = MiniGameSessionStatus.Abandoned;
            previous.UpdatedAt = now;
            repo.Update(previous);
        }

        var session = new MiniGameSession
        {
            UserId = userId,
            GameKey = DumbbellDashGameKey,
            EngineVersion = DumbbellDashSimulator.EngineVersion,
            Seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)),
            Status = MiniGameSessionStatus.Started,
            StartedAt = now,
            ExpiresAt = now + _options.SessionTimeout,
            CreatedAt = now,
        };
        await repo.AddAsync(session, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            // Ayni anda baska bir oyun baslatildi ya da onceki oyun bu sirada bitirildi.
            _unitOfWork.ClearTracking();
            throw new ConflictException("Oyun şu anda başlatılamadı. Lütfen tekrar dene.");
        }

        return new MiniGameSessionDto
        {
            RewardsEnabled = true,
            SessionId = session.Id,
            Seed = session.Seed,
            EngineVersion = session.EngineVersion,
            ExpiresAt = session.ExpiresAt,
            Rules = BuildRules(),
            EarnedToday = earnedToday,
            RemainingToday = Math.Max(0, _options.DailyPointLimit - earnedToday),
        };
    }

    public async Task<MiniGameResultDto> FinishSessionAsync(long userId, long sessionId, FinishMiniGameSessionRequest request, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<MiniGameSession>();
        var session = await repo.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
        if (session is null)
        {
            throw new NotFoundException(nameof(MiniGameSession), sessionId);
        }

        if (session.Status != MiniGameSessionStatus.Started)
        {
            // Tekrar gonderim (orn. baglanti koptu, istemci yeniden denedi): ilk sonuc, ikinci kez puan verilmeden.
            return await BuildResultAsync(session, null, cancellationToken);
        }

        // Bu oyunun puani eklenmeden onceki toplam (sonucta "onceki -> yeni toplam" gosterilir).
        var previousTotal = await GetTotalPointsAsync(userId, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var flapTicks = request.FlapTicks ?? new List<int>();
        session.ClaimedScore = request.Score;
        session.InputCount = flapTicks.Count;
        session.CompletedAt = now;
        session.UpdatedAt = now;

        if (now > session.ExpiresAt)
        {
            session.Status = MiniGameSessionStatus.Expired;
        }
        else
        {
            var rejectReason = Verify(session, request, flapTicks, now, out var replay);
            if (rejectReason is not null)
            {
                session.Status = MiniGameSessionStatus.Rejected;
                session.RejectReason = rejectReason;
                _logger.LogWarning(
                    "Mini oyun sonucu reddedildi. UserId={UserId} SessionId={SessionId} Reason={Reason} ClaimedScore={ClaimedScore}",
                    userId, sessionId, rejectReason, request.Score);
            }
            else
            {
                var dayStart = _scoreService.GetLocalDayStartUtc(now);
                var earnedToday = await GetEarnedTodayAsync(userId, dayStart, cancellationToken);
                session.Status = MiniGameSessionStatus.Completed;
                session.VerifiedScore = replay!.Score;
                session.VerifiedTicks = replay.Ticks;
                session.TierPoints = _options.PointsForScore(replay.Score);
                session.AwardedPoints = Math.Min(session.TierPoints, Math.Max(0, _options.DailyPointLimit - earnedToday));
            }
        }

        repo.Update(session);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ayni oturum eszamanli olarak baska bir istekle sonuclandirildi (cift gonderim) veya yeni oyun
            // baslatilarak terk edildi: kazanan istegin kaydettigi sonucu dondur, burada puan verme.
            _unitOfWork.ClearTracking();
            var stored = await repo.GetQueryable()
                .FirstAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
            return await BuildResultAsync(stored, null, cancellationToken);
        }

        if (session.AwardedPoints > 0)
        {
            // Basarilar ve MerfitScore/History ozeti (liderlik, profil, puan gecmisi) guncellenir.
            await _scoreService.TryRefreshUserAsync(userId, cancellationToken);
        }

        return await BuildResultAsync(session, previousTotal, cancellationToken);
    }

    /// <summary>Gecerliyse null, degilse red sebebi.</summary>
    private string? Verify(MiniGameSession session, FinishMiniGameSessionRequest request, IReadOnlyList<int> flapTicks, DateTime now, out DumbbellDashReplayResult? replay)
    {
        replay = null;
        if (session.EngineVersion != DumbbellDashSimulator.EngineVersion)
        {
            return "engine_version";
        }

        if (flapTicks.Count > DumbbellDashSimulator.MaxTicks || request.Ticks < 0 || request.Ticks > DumbbellDashSimulator.MaxTicks)
        {
            return "invalid_input";
        }

        var result = DumbbellDashSimulator.Replay((uint)session.Seed, flapTicks);
        if (!result.Valid)
        {
            return result.Reason;
        }

        if (result.Score != request.Score || result.Ticks != request.Ticks)
        {
            return "score_mismatch";
        }

        // Oyun, oturum acildiktan sonra gercek zamanda oynanmis olmali (hizlandirilmis/otomatik uretilmis sonuc).
        var simulated = TimeSpan.FromSeconds((double)result.Ticks / DumbbellDashSimulator.TickRate);
        if (now - session.StartedAt + _options.ClockTolerance < simulated)
        {
            return "too_fast";
        }

        replay = result;
        return null;
    }

    private async Task<MiniGameResultDto> BuildResultAsync(MiniGameSession session, int? previousTotal, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var earnedToday = await GetEarnedTodayAsync(session.UserId, _scoreService.GetLocalDayStartUtc(now), cancellationToken);
        var total = await GetTotalPointsAsync(session.UserId, cancellationToken);
        var nextTier = session.VerifiedScore.HasValue ? _options.NextTier(session.VerifiedScore.Value) : null;

        return new MiniGameResultDto
        {
            SessionId = session.Id,
            Status = MapStatus(session),
            VerifiedScore = session.VerifiedScore,
            TierPoints = session.TierPoints,
            AwardedPoints = session.AwardedPoints,
            DailyLimitReached = session.Status == MiniGameSessionStatus.Completed && session.AwardedPoints < session.TierPoints,
            PreviousTotalPoints = previousTotal ?? total - session.AwardedPoints,
            TotalPoints = total,
            EarnedToday = earnedToday,
            RemainingToday = Math.Max(0, _options.DailyPointLimit - earnedToday),
            NextTier = nextTier is null ? null : new MiniGameRewardTierDto { MinScore = nextTier.MinScore, Points = Math.Min(nextTier.Points, _options.MaxPointsPerGame) },
        };
    }

    private static string MapStatus(MiniGameSession session) => session.Status switch
    {
        MiniGameSessionStatus.Completed when session.AwardedPoints > 0 => MiniGameResultStatus.Rewarded,
        MiniGameSessionStatus.Completed when session.TierPoints > 0 => MiniGameResultStatus.DailyLimitReached,
        MiniGameSessionStatus.Completed => MiniGameResultStatus.NoReward,
        MiniGameSessionStatus.Expired => MiniGameResultStatus.Expired,
        MiniGameSessionStatus.Abandoned => MiniGameResultStatus.Abandoned,
        _ => MiniGameResultStatus.Rejected,
    };

    private MiniGameRewardRulesDto BuildRules() => new()
    {
        Tiers = _options.EffectiveTiers
            .Select(t => new MiniGameRewardTierDto { MinScore = t.MinScore, Points = Math.Min(t.Points, _options.MaxPointsPerGame) })
            .ToList(),
        MaxPointsPerGame = _options.MaxPointsPerGame,
        DailyPointLimit = _options.DailyPointLimit,
    };

    private async Task<int> GetEarnedTodayAsync(long userId, DateTime dayStartUtc, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<MiniGameSession>().GetQueryable()
            .Where(s => s.UserId == userId && s.Status == MiniGameSessionStatus.Completed && s.CompletedAt >= dayStartUtc)
            .SumAsync(s => s.AwardedPoints, cancellationToken);

    private async Task<int> GetTotalPointsAsync(long userId, CancellationToken cancellationToken)
    {
        var events = await _scoreService.GetPointEventsAsync(null, null, userId, cancellationToken);
        return events.Sum(e => e.Points);
    }
}
