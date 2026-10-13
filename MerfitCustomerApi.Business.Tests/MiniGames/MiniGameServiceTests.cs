using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.MiniGames;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.MiniGames;

/// <summary>
/// Mini oyun odul akisi: oturum acma, sunucuda yeniden oynatma ile dogrulama, odul tablosu, oyun basina ve
/// gunluk limitler, tekrar gonderimde ikinci kez puan verilmemesi ve eszamanlilik catismasi.
/// </summary>
public class MiniGameServiceTests
{
    private const long UserId = 7;
    private const long OtherUserId = 8;
    private const int BasePoints = 1_000;

    private sealed record GoldenGame(uint Seed, int[] FlapTicks, int Score, int Ticks);
    private sealed record Golden(GoldenGame[] Games);

    private static readonly GoldenGame[] Games = JsonSerializer.Deserialize<Golden>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MiniGames", "DumbbellDashGolden.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Games;

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly List<MiniGameSession> _sessions;
    private readonly Mock<IMerfitScoreService> _scoreService = new();
    private readonly FakeTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero) };
    private MiniGameRewardOptions _options = new();

    public MiniGameServiceTests()
    {
        _sessions = _uow.Set<MiniGameSession>();
        var scoring = new MerfitScoringOptions();
        _scoreService.Setup(s => s.GetLocalDayStartUtc(It.IsAny<DateTime>()))
            .Returns((DateTime utc) => DateTime.SpecifyKind((utc + TimeSpan.FromHours(scoring.TimeZoneOffsetHours)).Date - TimeSpan.FromHours(scoring.TimeZoneOffsetHours), DateTimeKind.Utc));
        // Toplam puan: sabit bir temel + kaydedilmis (Completed) mini oyun odulleri.
        _scoreService.Setup(s => s.GetPointEventsAsync(null, null, It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime? _, DateTime? _, long? userId, CancellationToken _) =>
                _sessions.Where(s => s.UserId == userId && s.Status == MiniGameSessionStatus.Completed && s.AwardedPoints > 0)
                    .Select(s => new MerfitPointEvent(s.UserId, s.CompletedAt!.Value, MerfitScoreService.MiniGameCategory, s.AwardedPoints))
                    .Prepend(new MerfitPointEvent(userId!.Value, DateTime.UtcNow, MerfitScoreService.WorkoutsCategory, BasePoints))
                    .ToList());
    }

    private DateTime Now => _clock.Now.UtcDateTime;

    private MiniGameService CreateService() => new(
        _uow.Object, _scoreService.Object, Options.Create(_options), _clock, NullLogger<MiniGameService>.Instance);

    private static GoldenGame GameWithScore(int score) => Games.Single(g => g.Score == score);

    /// <summary>Verilen golden oyunun seed'iyle bir oturum acar ve oyunun gercek suresi kadar zamani ilerletir.</summary>
    private async Task<(long SessionId, FinishMiniGameSessionRequest Request)> PlayAsync(int score, long userId = UserId)
    {
        var game = GameWithScore(score);
        var session = await CreateService().StartSessionAsync(userId, new StartMiniGameSessionRequest { EngineVersion = 1 });
        // Testte seed'i bilinen golden oyunla degistiriyoruz (sunucu rastgele uretir).
        _sessions.Single(s => s.Id == session.SessionId).Seed = game.Seed;
        _clock.Now = _clock.Now.AddSeconds(Math.Ceiling(game.Ticks / 60.0) + 1);
        return (session.SessionId!.Value, new FinishMiniGameSessionRequest { Score = game.Score, Ticks = game.Ticks, FlapTicks = game.FlapTicks.ToList() });
    }

    private void AddCompletedToday(int awarded) => _sessions.Add(new MiniGameSession
    {
        UserId = UserId, GameKey = "dumbbell-dash", EngineVersion = 1, Status = MiniGameSessionStatus.Completed,
        StartedAt = Now.AddMinutes(-30), ExpiresAt = Now.AddMinutes(-15), CompletedAt = Now.AddMinutes(-29),
        TierPoints = awarded, AwardedPoints = awarded, CreatedAt = Now.AddMinutes(-30),
    });

    [Fact]
    public async Task Start_ReturnsServerSeedRulesAndRemainingDailyPoints()
    {
        AddCompletedToday(40);

        var result = await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });

        Assert.True(result.RewardsEnabled);
        Assert.NotNull(result.SessionId);
        Assert.InRange(result.Seed!.Value, 0, uint.MaxValue);
        Assert.Equal(new[] { 10, 20, 30, 50 }, result.Rules.Tiers.Select(t => t.MinScore));
        Assert.Equal(new[] { 10, 20, 30, 50 }, result.Rules.Tiers.Select(t => t.Points));
        Assert.Equal(40, result.EarnedToday);
        Assert.Equal(110, result.RemainingToday);
        var stored = _sessions.Single(s => s.Id == result.SessionId);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal(MiniGameSessionStatus.Started, stored.Status);
        Assert.Equal(Now.AddMinutes(15), stored.ExpiresAt);
    }

    [Fact]
    public async Task Start_AbandonsPreviousUnfinishedSession()
    {
        var first = await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });
        var second = await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });

        Assert.Equal(MiniGameSessionStatus.Abandoned, _sessions.Single(s => s.Id == first.SessionId).Status);
        Assert.Equal(MiniGameSessionStatus.Started, _sessions.Single(s => s.Id == second.SessionId).Status);
        Assert.Single(_sessions, s => s.UserId == UserId && s.Status == MiniGameSessionStatus.Started);
    }

    [Fact]
    public async Task Start_RejectsOutdatedEngineVersion()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 99 }));
    }

    [Fact]
    public async Task Start_EnforcesDailySessionLimit()
    {
        _options = new MiniGameRewardOptions { MaxSessionsPerDay = 2 };
        await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });
        await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });

        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 }));
    }

    [Fact]
    public async Task Start_WhenRewardsDisabled_DoesNotCreateSession()
    {
        _options = new MiniGameRewardOptions { Enabled = false };

        var result = await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });

        Assert.False(result.RewardsEnabled);
        Assert.Null(result.SessionId);
        Assert.Empty(_sessions);
    }

    [Theory]
    [InlineData(0, 0, MiniGameResultStatus.NoReward, 10)]
    [InlineData(7, 0, MiniGameResultStatus.NoReward, 10)]
    [InlineData(13, 10, MiniGameResultStatus.Rewarded, 20)]
    [InlineData(25, 20, MiniGameResultStatus.Rewarded, 30)]
    [InlineData(52, 50, MiniGameResultStatus.Rewarded, null)]
    public async Task Finish_AwardsPointsForServerVerifiedScore(int score, int expectedPoints, string expectedStatus, int? expectedNextTier)
    {
        var (sessionId, request) = await PlayAsync(score);

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(score, result.VerifiedScore);
        Assert.Equal(expectedPoints, result.AwardedPoints);
        Assert.Equal(BasePoints, result.PreviousTotalPoints);
        Assert.Equal(BasePoints + expectedPoints, result.TotalPoints);
        Assert.Equal(expectedNextTier, result.NextTier?.MinScore);
        Assert.Equal(expectedPoints, result.EarnedToday);
        _scoreService.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), expectedPoints > 0 ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Finish_SubmittedTwice_ReturnsSameResultWithoutSecondAward()
    {
        var (sessionId, request) = await PlayAsync(52);

        var first = await CreateService().FinishSessionAsync(UserId, sessionId, request);
        var second = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(50, first.AwardedPoints);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.AwardedPoints, second.AwardedPoints);
        Assert.Equal(first.TotalPoints, second.TotalPoints);
        Assert.Equal(BasePoints, second.PreviousTotalPoints);
        Assert.Equal(50, _sessions.Where(s => s.UserId == UserId).Sum(s => s.AwardedPoints));
        _scoreService.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Finish_ClaimedScoreHigherThanReplay_IsRejected()
    {
        var (sessionId, request) = await PlayAsync(13);
        request.Score = 500;

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Rejected, result.Status);
        Assert.Equal(0, result.AwardedPoints);
        Assert.Equal("score_mismatch", _sessions.Single(s => s.Id == sessionId).RejectReason);
    }

    [Fact]
    public async Task Finish_FabricatedInput_IsRejected()
    {
        var (sessionId, request) = await PlayAsync(13);
        request.FlapTicks = new List<int> { 10, 5 };

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Rejected, result.Status);
        Assert.Equal("invalid_input", _sessions.Single(s => s.Id == sessionId).RejectReason);
    }

    [Fact]
    public async Task Finish_FasterThanRealTime_IsRejected()
    {
        var game = GameWithScore(52);
        var session = await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });
        _sessions.Single(s => s.Id == session.SessionId).Seed = game.Seed;
        // 52 skorluk oyun ~88 sn surer; sonuc 10 sn sonra geliyor.
        _clock.Now = _clock.Now.AddSeconds(10);

        var result = await CreateService().FinishSessionAsync(UserId, session.SessionId!.Value,
            new FinishMiniGameSessionRequest { Score = game.Score, Ticks = game.Ticks, FlapTicks = game.FlapTicks.ToList() });

        Assert.Equal(MiniGameResultStatus.Rejected, result.Status);
        Assert.Equal("too_fast", _sessions.Single(s => s.Id == session.SessionId).RejectReason);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public async Task Finish_AfterSessionTimeout_IsExpired()
    {
        var (sessionId, request) = await PlayAsync(52);
        _clock.Now = _clock.Now.AddMinutes(20);

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Expired, result.Status);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public async Task Finish_AppliesDailyLimit()
    {
        AddCompletedToday(50);
        AddCompletedToday(50);
        AddCompletedToday(30);

        var (sessionId, request) = await PlayAsync(52);
        var partial = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Rewarded, partial.Status);
        Assert.Equal(50, partial.TierPoints);
        Assert.Equal(20, partial.AwardedPoints);
        Assert.True(partial.DailyLimitReached);
        Assert.Equal(150, partial.EarnedToday);
        Assert.Equal(0, partial.RemainingToday);

        var (nextId, nextRequest) = await PlayAsync(25);
        var blocked = await CreateService().FinishSessionAsync(UserId, nextId, nextRequest);

        Assert.Equal(MiniGameResultStatus.DailyLimitReached, blocked.Status);
        Assert.Equal(0, blocked.AwardedPoints);
        Assert.Equal(150, _sessions.Where(s => s.UserId == UserId).Sum(s => s.AwardedPoints));
    }

    [Fact]
    public async Task Finish_DailyLimitResetsOnNextLocalDay()
    {
        AddCompletedToday(150);
        // Yerel gun (UTC+3) bitiyor: 21:00 UTC = 00:00 yerel.
        _clock.Now = new DateTimeOffset(2026, 10, 13, 21, 0, 0, TimeSpan.Zero);

        var (sessionId, request) = await PlayAsync(13);
        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(10, result.AwardedPoints);
    }

    [Fact]
    public async Task Finish_OtherUsersSession_IsNotFound()
    {
        var (sessionId, request) = await PlayAsync(52, OtherUserId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().FinishSessionAsync(UserId, sessionId, request));
        Assert.Equal(MiniGameSessionStatus.Started, _sessions.Single(s => s.Id == sessionId).Status);
    }

    [Fact]
    public async Task Finish_AbandonedSession_IsNotRewarded()
    {
        var (sessionId, request) = await PlayAsync(52);
        await CreateService().StartSessionAsync(UserId, new StartMiniGameSessionRequest { EngineVersion = 1 });

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Abandoned, result.Status);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public async Task Finish_ConcurrencyConflict_ReturnsStoredResultWithoutAward()
    {
        var (sessionId, request) = await PlayAsync(52);
        // Baska bir istek ayni oturumu bu istekten once sonuclandirdi: veritabanindaki satir Completed/50 puan,
        // bu istegin SaveChanges'i concurrency token (Status) yuzunden basarisiz olur.
        var stored = new MiniGameSession
        {
            Id = sessionId, UserId = UserId, Status = MiniGameSessionStatus.Completed, VerifiedScore = 52,
            TierPoints = 50, AwardedPoints = 50, CompletedAt = Now,
        };
        _uow.Mock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _sessions.RemoveAll(s => s.Id == sessionId);
                _sessions.Add(stored);
            })
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict"));

        var result = await CreateService().FinishSessionAsync(UserId, sessionId, request);

        Assert.Equal(MiniGameResultStatus.Rewarded, result.Status);
        Assert.Equal(50, result.AwardedPoints);
        _uow.Mock.Verify(u => u.ClearTracking(), Times.Once);
        _scoreService.Verify(s => s.TryRefreshUserAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(50, _sessions.Where(s => s.UserId == UserId).Sum(s => s.AwardedPoints));
    }
}
