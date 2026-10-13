using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;
using MerfitCustomerApi.Business.Services.MiniGames;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.MiniGames;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir (bkz. PostgresFactAttribute). Gercek servis, gercek
/// UnitOfWork ve gercek PostgreSQL ile: ayni sonucun eszamanli gonderiminde tek odul, eszamanli oyun
/// baslatmada tek aktif oturum ve odulun MB FIT puan motoruna yansimasi.
/// </summary>
public class MiniGameSessionPostgresTests
{
    private sealed record GoldenGame(uint Seed, int[] FlapTicks, int Score, int Ticks);
    private sealed record Golden(GoldenGame[] Games);

    private static readonly GoldenGame Game52 = JsonSerializer.Deserialize<Golden>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MiniGames", "DumbbellDashGolden.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Games.Single(g => g.Score == 52);

    private sealed class ShiftedTimeProvider : TimeProvider
    {
        private readonly TimeSpan _shift;
        public ShiftedTimeProvider(TimeSpan shift) => _shift = shift;
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _shift;
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static (MiniGameService Service, AppDbContext Db) CreateService(TimeSpan shift = default)
    {
        var db = CreateContext();
        var uow = new UnitOfWork(db);
        var scoring = new MerfitScoreService(uow, Options.Create(new MerfitScoringOptions()), NullLogger<MerfitScoreService>.Instance);
        var service = new MiniGameService(uow, scoring, Options.Create(new MiniGameRewardOptions()), new ShiftedTimeProvider(shift), NullLogger<MiniGameService>.Instance);
        return (service, db);
    }

    private static async Task<long> CreateUserAsync()
    {
        await using var db = CreateContext();
        var email = $"minigame-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Golden 52 skorluk oyunun seed'iyle bir oturum hazirlar.</summary>
    private static async Task<long> StartWithGoldenSeedAsync(long userId)
    {
        var (service, db) = CreateService();
        await using (db)
        {
            var session = await service.StartSessionAsync(userId, new StartMiniGameSessionRequest { EngineVersion = 1 });
            var row = await db.MiniGameSessions.SingleAsync(s => s.Id == session.SessionId);
            row.Seed = Game52.Seed;
            await db.SaveChangesAsync();
            return row.Id;
        }
    }

    private static FinishMiniGameSessionRequest Request => new() { Score = Game52.Score, Ticks = Game52.Ticks, FlapTicks = Game52.FlapTicks.ToList() };

    [PostgresFact]
    public async Task ConcurrentDuplicateFinish_AwardsExactlyOnce()
    {
        var userId = await CreateUserAsync();
        var sessionId = await StartWithGoldenSeedAsync(userId);

        // Oyun ~88 sn surer; saat ileri alinarak gercek sure gecmis gibi davranilir.
        async Task<MiniGameResultDto> FinishAsync()
        {
            var (service, db) = CreateService(TimeSpan.FromMinutes(2));
            await using (db)
            {
                return await service.FinishSessionAsync(userId, sessionId, Request);
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(FinishAsync)));

        Assert.All(results, r => Assert.Equal(MiniGameResultStatus.Rewarded, r.Status));
        Assert.All(results, r => Assert.Equal(5, r.AwardedPoints));
        await using var check = CreateContext();
        var awarded = await check.MiniGameSessions.Where(s => s.UserId == userId).SumAsync(s => s.AwardedPoints);
        Assert.Equal(5, awarded);
    }

    [PostgresFact]
    public async Task ConcurrentStarts_LeaveAtMostOneActiveSession()
    {
        var userId = await CreateUserAsync();

        async Task<bool> StartAsync()
        {
            var (service, db) = CreateService();
            await using (db)
            {
                try
                {
                    await service.StartSessionAsync(userId, new StartMiniGameSessionRequest { EngineVersion = 1 });
                    return true;
                }
                catch (ConflictException)
                {
                    return false;
                }
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(StartAsync)));

        Assert.Contains(true, results);
        await using var check = CreateContext();
        Assert.Equal(1, await check.MiniGameSessions.CountAsync(s => s.UserId == userId && s.Status == MiniGameSessionStatus.Started));
    }

    [PostgresFact]
    public async Task AwardedPoints_AppearInScoreEngineAndSnapshot()
    {
        var userId = await CreateUserAsync();
        var sessionId = await StartWithGoldenSeedAsync(userId);

        var (service, db) = CreateService(TimeSpan.FromMinutes(2));
        await using (db)
        {
            var result = await service.FinishSessionAsync(userId, sessionId, Request);
            Assert.Equal(0, result.PreviousTotalPoints);
            Assert.Equal(5, result.TotalPoints);
        }

        await using var check = CreateContext();
        var snapshot = await check.MerfitScores.SingleAsync(s => s.UserId == userId && s.Period == MerfitScoreService.AllTimePeriodTag);
        Assert.Equal(5, snapshot.Score);
        var breakdown = await check.Set<MerfitScoreBreakdown>().SingleAsync(b => b.MerfitScoreId == snapshot.Id);
        Assert.Equal(MerfitScoreService.MiniGameCategory, breakdown.Category);
    }
}
