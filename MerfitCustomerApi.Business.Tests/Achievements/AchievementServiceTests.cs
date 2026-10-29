using MerfitCustomerApi.Business.Dtos.Customer.Achievements;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Achievements;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Achievements;

/// <summary>Basari listesi ve yeni kazanim kutlamasinin her kazanim icin bir kez gosterilmesi.</summary>
public class AchievementServiceTests
{
    private const long UserId = 31;
    private const long OtherUserId = 32;

    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<IMerfitScoreService> _score = new();

    public AchievementServiceTests()
    {
        _db.Set<Achievement>().AddRange(
            new Achievement { Id = 1, Code = "FIRST_WORKOUT", Title = "İlk Antrenman", Icon = "🏁", Points = 10, IsActive = true, ConditionType = "workouts_completed", ConditionValue = "1" },
            new Achievement { Id = 2, Code = "STREAK_7", Title = "7 Günlük Seri", Icon = "streak", Points = 25, IsActive = true, ConditionType = "streak_days", ConditionValue = "7" },
            new Achievement { Id = 3, Code = "PASSIVE", Title = "Pasif", Points = 5, IsActive = false, ConditionType = "workouts_completed", ConditionValue = "1" });
        _db.Register<UserAchievement>();
    }

    private AchievementService CreateService() => new(_db.Object, _score.Object);

    [Fact]
    public async Task Get_RefreshesConditions_AndReturnsOnlyUnseenAsNew()
    {
        _db.Set<UserAchievement>().AddRange(
            new UserAchievement { Id = 1, UserId = UserId, AchievementId = 1, EarnedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), SeenAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) },
            new UserAchievement { Id = 2, UserId = UserId, AchievementId = 2, EarnedAt = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc) },
            new UserAchievement { Id = 3, UserId = OtherUserId, AchievementId = 1, EarnedAt = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc) });

        var result = await CreateService().GetMyAchievementsAsync(UserId);

        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.TotalCount);       // pasif basari listelenmez
        Assert.Equal(2, result.EarnedCount);
        var fresh = Assert.Single(result.NewlyEarned);
        Assert.Equal("STREAK_7", fresh.Code);
        Assert.Equal(25, fresh.Points);
        Assert.DoesNotContain(result.Achievements, a => a.Code == "PASSIVE");
    }

    [Fact]
    public async Task Get_NotEarned_IsListedButNotNew()
    {
        var result = await CreateService().GetMyAchievementsAsync(UserId);

        Assert.Equal(0, result.EarnedCount);
        Assert.Empty(result.NewlyEarned);
        Assert.All(result.Achievements, a => Assert.False(a.Earned));
        Assert.All(result.Achievements, a => Assert.Null(a.EarnedAt));
    }

    [Fact]
    public async Task Get_DuplicateLegacyRows_AreCollapsed()
    {
        _db.Set<UserAchievement>().AddRange(
            new UserAchievement { Id = 1, UserId = UserId, AchievementId = 1, EarnedAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), SeenAt = DateTime.UtcNow },
            new UserAchievement { Id = 2, UserId = UserId, AchievementId = 1, EarnedAt = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc) });

        var result = await CreateService().GetMyAchievementsAsync(UserId);

        Assert.Equal(1, result.EarnedCount);
        Assert.Empty(result.NewlyEarned);
        Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), result.Achievements.Single(a => a.Id == 1).EarnedAt);
    }

    [Fact]
    public async Task MarkSeen_OnlyTouchesOwnUnseenRows_AndIsIdempotent()
    {
        var seenEarlier = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.Set<UserAchievement>().AddRange(
            new UserAchievement { Id = 1, UserId = UserId, AchievementId = 1, EarnedAt = seenEarlier, SeenAt = seenEarlier },
            new UserAchievement { Id = 2, UserId = UserId, AchievementId = 2, EarnedAt = DateTime.UtcNow },
            new UserAchievement { Id = 3, UserId = OtherUserId, AchievementId = 2, EarnedAt = DateTime.UtcNow });
        var service = CreateService();

        await service.MarkSeenAsync(UserId, new MarkAchievementsSeenRequest { AchievementIds = new() { 1, 2, 2 } });
        var firstSaves = _db.SaveCount;
        await service.MarkSeenAsync(UserId, new MarkAchievementsSeenRequest { AchievementIds = new() { 2 } });

        var rows = _db.Set<UserAchievement>();
        Assert.Equal(seenEarlier, rows.Single(r => r.Id == 1).SeenAt);
        Assert.NotNull(rows.Single(r => r.Id == 2).SeenAt);
        Assert.Null(rows.Single(r => r.Id == 3).SeenAt);   // baska kullanicinin kaydi
        Assert.Equal(1, firstSaves);
        Assert.Equal(firstSaves, _db.SaveCount);           // ikinci cagri bir sey yazmaz
    }
}
