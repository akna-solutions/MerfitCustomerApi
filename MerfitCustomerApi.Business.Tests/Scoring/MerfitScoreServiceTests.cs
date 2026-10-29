using System.Linq.Expressions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Scoring;

/// <summary>
/// MB FIT puan motoru: aksiyonlardan puan turetme, gunluk ust sinirlar, siralama, donem araligi,
/// basari verme ve MerfitScore ozetinin guncellenmesi.
/// </summary>
public class MerfitScoreServiceTests
{
    private const long UserA = 1;
    private const long UserB = 2;
    private const long UserC = 3;

    private readonly List<WorkoutSession> _sessions = new();
    private readonly List<PersonalRecord> _records = new();
    private readonly List<Meal> _meals = new();
    private readonly List<MealItem> _mealItems = new();
    private readonly List<WaterLog> _waterLogs = new();
    private readonly List<Achievement> _achievements = new();
    private readonly List<UserAchievement> _userAchievements = new();
    private readonly List<LeaderboardPeriod> _periods = new();
    private readonly List<UserStreak> _streaks = new();
    private readonly List<MerfitScore> _scores = new();
    private readonly List<MerfitScoreBreakdown> _breakdowns = new();
    private readonly List<MerfitScoreHistory> _history = new();
    private readonly List<MiniGameSession> _miniGames = new();

    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public MerfitScoreServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(_sessions).Object);
        _unitOfWork.Setup(u => u.Repository<PersonalRecord>()).Returns(MockRepositoryFactory.Create(_records).Object);
        _unitOfWork.Setup(u => u.Repository<Meal>()).Returns(MockRepositoryFactory.Create(_meals).Object);
        _unitOfWork.Setup(u => u.Repository<MealItem>()).Returns(MockRepositoryFactory.Create(_mealItems).Object);
        _unitOfWork.Setup(u => u.Repository<WaterLog>()).Returns(MockRepositoryFactory.Create(_waterLogs).Object);
        _unitOfWork.Setup(u => u.Repository<Achievement>()).Returns(MockRepositoryFactory.Create(_achievements).Object);
        _unitOfWork.Setup(u => u.Repository<UserAchievement>()).Returns(MockRepositoryFactory.Create(_userAchievements).Object);
        _unitOfWork.Setup(u => u.Repository<LeaderboardPeriod>()).Returns(MockRepositoryFactory.Create(_periods).Object);
        _unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(_streaks).Object);
        _unitOfWork.Setup(u => u.Repository<MerfitScore>()).Returns(MockRepositoryFactory.Create(_scores).Object);
        _unitOfWork.Setup(u => u.Repository<MerfitScoreHistory>()).Returns(MockRepositoryFactory.Create(_history).Object);
        _unitOfWork.Setup(u => u.Repository<MiniGameSession>()).Returns(MockRepositoryFactory.Create(_miniGames).Object);

        var breakdownRepo = MockRepositoryFactory.Create(_breakdowns);
        breakdownRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<MerfitScoreBreakdown, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<MerfitScoreBreakdown, bool>> predicate, CancellationToken _) =>
                _breakdowns.AsQueryable().Where(predicate).ToList());
        _unitOfWork.Setup(u => u.Repository<MerfitScoreBreakdown>()).Returns(breakdownRepo.Object);

        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                foreach (var score in _scores.Where(s => s.Id == 0))
                {
                    score.Id = _scores.Max(s => s.Id) + 1;
                }
            })
            .ReturnsAsync(1);
    }

    private MerfitScoreService CreateService() => new(
        _unitOfWork.Object,
        Options.Create(new MerfitScoringOptions()),
        NullLogger<MerfitScoreService>.Instance);

    private void AddCompletedWorkout(long userId, DateTime completedAt) => _sessions.Add(new WorkoutSession
    {
        Id = _sessions.Count + 1, UserId = userId, WorkoutId = 1, StartedAt = completedAt.AddMinutes(-30),
        CompletedAt = completedAt, Status = WorkoutSessionStatus.Completed,
    });

    [Fact]
    public async Task PointEvents_CapScoredWorkoutsPerDay_AndCountMealAndWaterOncePerDay()
    {
        // Yerel saatle (UTC+3) ayni gun: 3 antrenman, en fazla 2'si puan alir.
        var day = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        AddCompletedWorkout(UserA, day);
        AddCompletedWorkout(UserA, day.AddHours(2));
        AddCompletedWorkout(UserA, day.AddHours(4));
        // Iptal edilen oturum puan almaz.
        _sessions.Add(new WorkoutSession { Id = 99, UserId = UserA, Status = WorkoutSessionStatus.Abandoned, CompletedAt = day });

        _records.Add(new PersonalRecord { Id = 1, UserId = UserA, AchievedAt = day });

        _meals.Add(new Meal { Id = 1, UserId = UserA, Date = day.Date.AddDays(-30) });
        _mealItems.Add(new MealItem { Id = 1, MealId = 1, CreatedAt = day });
        _mealItems.Add(new MealItem { Id = 2, MealId = 1, CreatedAt = day.AddHours(1) });

        _waterLogs.Add(new WaterLog { Id = 1, UserId = UserA, CreatedAt = day });
        _waterLogs.Add(new WaterLog { Id = 2, UserId = UserA, CreatedAt = day.AddHours(3) });

        var events = await CreateService().GetPointEventsAsync(null, null, UserA);

        Assert.Equal(200, events.Where(e => e.Category == MerfitScoreService.WorkoutsCategory).Sum(e => e.Points));
        Assert.Equal(25, events.Where(e => e.Category == MerfitScoreService.PersonalRecordsCategory).Sum(e => e.Points));
        Assert.Equal(10, events.Where(e => e.Category == MerfitScoreService.NutritionCategory).Sum(e => e.Points));
        Assert.Equal(5, events.Where(e => e.Category == MerfitScoreService.WaterCategory).Sum(e => e.Points));
    }

    [Fact]
    public async Task PointEvents_IncludeOnlyAwardedCompletedMiniGames()
    {
        var day = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        _miniGames.Add(new MiniGameSession { Id = 1, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 5, AwardedPoints = 5 });
        _miniGames.Add(new MiniGameSession { Id = 2, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day.AddHours(1), TierPoints = 3, AwardedPoints = 2 });
        // Puansiz, reddedilen, suresi dolan ve baska kullaniciya ait oturumlar sayilmaz.
        _miniGames.Add(new MiniGameSession { Id = 3, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 0, AwardedPoints = 0 });
        _miniGames.Add(new MiniGameSession { Id = 4, UserId = UserA, Status = MiniGameSessionStatus.Rejected, CompletedAt = day, TierPoints = 5, AwardedPoints = 0 });
        _miniGames.Add(new MiniGameSession { Id = 5, UserId = UserA, Status = MiniGameSessionStatus.Started });
        _miniGames.Add(new MiniGameSession { Id = 6, UserId = UserB, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 1, AwardedPoints = 1 });

        var events = await CreateService().GetPointEventsAsync(null, null, UserA);

        var miniGame = events.Where(e => e.Category == MerfitScoreService.MiniGameCategory).ToList();
        Assert.Equal(2, miniGame.Count);
        Assert.Equal(7, miniGame.Sum(e => e.Points));
        Assert.Contains(miniGame, e => e.OccurredAtUtc == day.AddHours(1));
    }

    [Fact]
    public async Task PointEvents_RespectWindow()
    {
        AddCompletedWorkout(UserA, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        AddCompletedWorkout(UserA, new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));

        var events = await CreateService().GetPointEventsAsync(
            new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), UserA);

        Assert.Single(events);
    }

    [Fact]
    public async Task RankUsers_OrdersByPoints_AndSkipsUsersWithoutPoints()
    {
        var day = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        AddCompletedWorkout(UserA, day);
        AddCompletedWorkout(UserB, day);
        AddCompletedWorkout(UserB, day.AddDays(1));
        _waterLogs.Add(new WaterLog { Id = 1, UserId = UserA, CreatedAt = day });

        var ranked = await CreateService().RankUsersAsync(null, null);

        Assert.Equal(new[] { UserB, UserA }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2 }, ranked.Select(r => r.Rank).ToArray());
        Assert.Equal(200, ranked[0].Points);
        Assert.Equal(2, ranked[0].Workouts);
        Assert.DoesNotContain(ranked, r => r.UserId == UserC);
    }

    [Fact]
    public async Task ResolveWindow_UsesActivePeriodCoveringToday()
    {
        var period = new LeaderboardPeriod
        {
            Id = 7, Type = LeaderboardPeriodType.Monthly, IsActive = true,
            StartDate = DateTime.UtcNow.AddDays(-3), EndDate = DateTime.UtcNow.AddDays(10),
        };
        _periods.Add(period);
        _periods.Add(new LeaderboardPeriod { Id = 8, Type = LeaderboardPeriodType.Monthly, IsActive = true, StartDate = DateTime.UtcNow.AddDays(-60), EndDate = DateTime.UtcNow.AddDays(-30) });

        var window = await CreateService().ResolveWindowAsync(LeaderboardPeriodType.Monthly);

        Assert.Same(period, window.Period);
        Assert.Equal(period.StartDate, window.FromUtc);
        Assert.Equal(period.EndDate, window.ToUtc);
    }

    [Fact]
    public async Task ResolveWindow_FallsBackToLocalCalendarWeek_StartingMonday()
    {
        var window = await CreateService().ResolveWindowAsync(LeaderboardPeriodType.Weekly);

        Assert.Null(window.Period);
        var localStart = window.FromUtc!.Value.AddHours(3);
        Assert.Equal(DayOfWeek.Monday, localStart.DayOfWeek);
        Assert.Equal(TimeSpan.Zero, localStart.TimeOfDay);
        Assert.Equal(TimeSpan.FromDays(7), window.ToUtc!.Value - window.FromUtc.Value);
        Assert.InRange(DateTime.UtcNow, window.FromUtc.Value, window.ToUtc.Value);
    }

    [Fact]
    public async Task ResolveWindow_AllTimeIsUnbounded()
    {
        var window = await CreateService().ResolveWindowAsync(LeaderboardPeriodType.AllTime);

        Assert.Null(window.FromUtc);
        Assert.Null(window.ToUtc);
    }

    [Fact]
    public async Task TryRefresh_AwardsMetAchievements_AndWritesAllTimeSnapshot()
    {
        AddCompletedWorkout(UserA, DateTime.UtcNow.AddDays(-1));
        _achievements.Add(new Achievement { Id = 1, Code = "FIRST", IsActive = true, Points = 50, ConditionType = "workouts_completed", ConditionValue = "1" });
        _achievements.Add(new Achievement { Id = 2, Code = "TEN", IsActive = true, Points = 50, ConditionType = "workouts_completed", ConditionValue = "10" });
        _achievements.Add(new Achievement { Id = 3, Code = "UNKNOWN", IsActive = true, Points = 50, ConditionType = "something_else", ConditionValue = "0" });
        _achievements.Add(new Achievement { Id = 4, Code = "PASSIVE", IsActive = false, Points = 50, ConditionType = "workouts_completed", ConditionValue = "1" });

        await CreateService().TryRefreshUserAsync(UserA);

        var earned = Assert.Single(_userAchievements);
        Assert.Equal(1, earned.AchievementId);

        var score = Assert.Single(_scores);
        Assert.Equal(MerfitScoreService.AllTimePeriodTag, score.Period);
        Assert.Equal(150m, score.Score);
        Assert.Equal(150m, _breakdowns.Where(b => b.MerfitScoreId == score.Id).Sum(b => b.Points));
        Assert.Equal(150m, Assert.Single(_history).Score);
    }

    [Fact]
    public async Task TryRefresh_IsIdempotent()
    {
        AddCompletedWorkout(UserA, DateTime.UtcNow.AddDays(-1));
        _achievements.Add(new Achievement { Id = 1, Code = "FIRST", IsActive = true, Points = 50, ConditionType = "workouts_completed", ConditionValue = "1" });

        var service = CreateService();
        await service.TryRefreshUserAsync(UserA);
        await service.TryRefreshUserAsync(UserA);

        Assert.Single(_userAchievements);
        Assert.Single(_scores);
        Assert.Single(_history);
        Assert.Equal(2, _breakdowns.Count);
    }

    [Theory]
    [InlineData("workout_count")]
    [InlineData("WORKOUTS")]
    [InlineData(" Workouts_Completed ")]
    public async Task TryRefresh_AcceptsLegacyConditionNames(string conditionType)
    {
        // Eski admin ekraninin kaydettigi kosul adlari daha once "desteklenmeyen" diye atlaniyordu; rozet hic kazanilmiyordu.
        AddCompletedWorkout(UserA, DateTime.UtcNow.AddDays(-1));
        _achievements.Add(new Achievement { Id = 7, Code = "FIRST_WORKOUT", IsActive = true, Points = 10, ConditionType = conditionType, ConditionValue = "1" });

        await CreateService().TryRefreshUserAsync(UserA);

        var earned = Assert.Single(_userAchievements);
        Assert.Equal(7, earned.AchievementId);
        Assert.Null(earned.SeenAt);
    }

    [Fact]
    public async Task TryRefresh_UniqueViolationFromConcurrentRequest_IsSwallowed()
    {
        AddCompletedWorkout(UserA, DateTime.UtcNow.AddDays(-1));
        _achievements.Add(new Achievement { Id = 1, Code = "FIRST", IsActive = true, Points = 50, ConditionType = "workouts_completed", ConditionValue = "1" });
        var saves = 0;
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++saves == 1
                ? throw new Microsoft.EntityFrameworkCore.DbUpdateException("dup", new FakePostgresException("23505"))
                : 1);

        await CreateService().TryRefreshUserAsync(UserA);

        // Ilk kayit tekil indekse takildi; puan ozeti yine de yazildi ve hata kullanicinin islemini bozmadi.
        _unitOfWork.Verify(u => u.ClearTracking(), Times.Once);
        Assert.Single(_scores);
    }

    [Theory]
    [InlineData("workout_count", MerfitScoreService.WorkoutsCompletedCondition)]
    [InlineData("streak", MerfitScoreService.StreakDaysCondition)]
    [InlineData("pr_count", MerfitScoreService.PersonalRecordsCondition)]
    [InlineData("meal_log_days", MerfitScoreService.MealLogDaysCondition)]
    [InlineData("early_workout_count", "early_workout_count")]
    [InlineData(null, "")]
    public void NormalizeConditionType_MapsLegacyAliases(string? input, string expected)
    {
        Assert.Equal(expected, MerfitScoreService.NormalizeConditionType(input));
    }

    /// <summary>DbExceptionClassifier'in reflection ile okudugu SqlState alanini taklit eder.</summary>
    private sealed class FakePostgresException(string sqlState) : Exception("postgres")
    {
        public string SqlState { get; } = sqlState;
        public string? ConstraintName => "IX_UserAchievement_UserId_AchievementId";
    }
}
