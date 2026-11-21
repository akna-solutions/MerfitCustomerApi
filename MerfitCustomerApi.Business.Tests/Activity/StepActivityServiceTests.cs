using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Activity;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Activity;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Activity;

/// <summary>
/// Adim sayar senkronizasyonu: idempotent birlestirme, tarih penceresi, makullik (anti-hile) isaretleri,
/// saat dilimi degisimi, kullanici izolasyonu, hedef ve haftalik ozet.
/// </summary>
public class StepActivityServiceTests
{
    private const long UserId = 7;
    private const long OtherUserId = 8;
    /// <summary>Turkiye (UTC+3).</summary>
    private const int Offset = 180;

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly List<DailyActivity> _days;
    private readonly List<UserActivitySetting> _settings;
    private readonly List<UserProfile> _profiles;
    private readonly Mock<IMerfitScoreService> _score = new();
    // 09:30 UTC = 12:30 yerel (UTC+3), 20 Kasim 2026.
    private readonly FakeTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero) };
    private static readonly DateOnly Today = new(2026, 11, 20);

    public StepActivityServiceTests()
    {
        _days = _uow.Set<DailyActivity>();
        _settings = _uow.Set<UserActivitySetting>();
        _profiles = _uow.Set<UserProfile>();
        _profiles.Add(new UserProfile { UserId = UserId, HeightCm = 180, WeightKg = 80, Gender = Gender.Male });
    }

    private StepActivityService CreateService() => new(
        _uow.Object, _score.Object, Options.Create(new StepActivityOptions()), _clock, NullLogger<StepActivityService>.Instance);

    private static int[] Hours(params (int Hour, int Steps)[] values)
    {
        var hours = new int[24];
        foreach (var (hour, steps) in values)
        {
            hours[hour] = steps;
        }

        return hours;
    }

    private static StepSyncRequest Request(DateOnly date, int[] hours, int offset = Offset) => new()
    {
        Source = "pedometer",
        Platform = "ios",
        Days = new List<StepSyncDayRequest> { new() { Date = date, HourlySteps = hours, UtcOffsetMinutes = offset } },
    };

    [Fact]
    public async Task Sync_NewDay_StoresHourlyAndDerivesTotalsOnServer()
    {
        var result = await CreateService().SyncAsync(UserId, Request(Today, Hours((8, 1200), (9, 3000), (11, 800))));

        var day = Assert.Single(result.Days);
        Assert.Equal(StepActivityService.StatusAccepted, day.Status);
        Assert.Equal(5000, day.TotalSteps);
        Assert.Equal(10_000, day.DailyGoal);
        Assert.Equal(50, day.CompletionPercent);
        Assert.True(day.IsVerified);

        var row = Assert.Single(_days);
        Assert.Equal(UserId, row.UserId);
        Assert.Equal(5000, row.TotalSteps);
        // 180 cm erkek: adim 0,747 m -> 3.735 m; kalori 3,735 km x 80 kg x 0,5 = 149 kcal.
        Assert.Equal(3735, row.DistanceMeters);
        Assert.Equal(149, row.CaloriesBurned);
        Assert.False(row.IsSuspicious);
        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sync_SameRequestTwice_DoesNotCountStepsTwice()
    {
        var hours = Hours((9, 3000), (10, 2000));
        await CreateService().SyncAsync(UserId, Request(Today, hours));
        var savesAfterFirst = _uow.SaveCount;

        var second = await CreateService().SyncAsync(UserId, Request(Today, hours.ToArray()));

        Assert.Equal(StepActivityService.StatusUnchanged, Assert.Single(second.Days).Status);
        Assert.Equal(5000, Assert.Single(_days).TotalSteps);
        Assert.Equal(savesAfterFirst, _uow.SaveCount);
        // Ikinci (degismeyen) istek basari hesabini tekrar tetiklemez.
        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sync_StaleCopyFromAnotherDevice_NeitherReducesNorAdds()
    {
        await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 3000), (10, 2000))));

        // Eski kopya: 9'da daha az, 11'de yeni adim. 9 korunur, 11 eklenir; toplam kovalarin toplamidir.
        var result = await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 1000), (11, 500))));

        Assert.Equal(5500, Assert.Single(result.Days).TotalSteps);
        Assert.Equal(new[] { 3000, 2000, 500 }, Assert.Single(_days).HourlySteps.Skip(9).Take(3));
    }

    [Fact]
    public async Task Sync_FutureOrTooOldDate_IsRejectedWithoutWriting()
    {
        var request = new StepSyncRequest
        {
            Source = "pedometer",
            Days = new List<StepSyncDayRequest>
            {
                new() { Date = Today.AddDays(1), HourlySteps = Hours((1, 100)), UtcOffsetMinutes = Offset },
                new() { Date = Today.AddDays(-8), HourlySteps = Hours((1, 100)), UtcOffsetMinutes = Offset },
            },
        };

        var result = await CreateService().SyncAsync(UserId, request);

        Assert.All(result.Days, d => Assert.Equal(StepActivityService.StatusRejected, d.Status));
        Assert.Equal(new[] { "future_date", "too_old" }, result.Days.Select(d => d.Reason));
        Assert.Empty(_days);
        _score.Verify(s => s.TryRefreshUserAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sync_LastWeekBackfill_IsAccepted()
    {
        // iOS CoreMotion son 7 gunu verir; uygulama gunlerce acilmasa da gecmis gunler yuklenebilir.
        var result = await CreateService().SyncAsync(UserId, Request(Today.AddDays(-7), Hours((18, 11_000))));

        Assert.Equal(StepActivityService.StatusAccepted, Assert.Single(result.Days).Status);
        Assert.False(Assert.Single(_days).IsSuspicious);
    }

    [Fact]
    public async Task Sync_StepsInHourThatHasNotHappenedYet_FlagsDayAndStaysFlagged()
    {
        // Yerel saat 12:30; 15:00 kovasinda adim olamaz.
        await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 1000), (15, 4000))));

        var row = Assert.Single(_days);
        Assert.True(row.IsSuspicious);
        Assert.Equal(StepActivityService.ReasonFutureHour, row.SuspiciousReason);

        // Saat 16:00 olduktan sonra ayni veri "normal" gorunse de isaret kalkmaz.
        _clock.Now = _clock.Now.AddHours(4);
        var later = await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 1000), (15, 4000), (16, 200))));
        Assert.False(Assert.Single(later.Days).IsVerified);
        Assert.True(row.IsSuspicious);
    }

    [Fact]
    public async Task Sync_HourAboveLimit_FlagsDayButKeepsSteps()
    {
        var result = await CreateService().SyncAsync(UserId, Request(Today.AddDays(-1), Hours((10, 25_000))));

        var day = Assert.Single(result.Days);
        Assert.Equal(25_000, day.TotalSteps);
        Assert.False(day.IsVerified);
        Assert.Equal(StepActivityService.ReasonHourLimit, Assert.Single(_days).SuspiciousReason);
    }

    [Fact]
    public async Task Sync_DayAboveLimit_FlagsDay()
    {
        var hours = Enumerable.Repeat(3000, 24).ToArray(); // 72.000 adim, her saat makul ama gun degil.

        await CreateService().SyncAsync(UserId, Request(Today.AddDays(-1), hours));

        Assert.Equal(StepActivityService.ReasonDayLimit, Assert.Single(_days).SuspiciousReason);
    }

    [Theory]
    [InlineData(8_000, false)]
    [InlineData(9_000, true)]
    public async Task Sync_CurrentHour_LimitedByElapsedMinutes(int stepsThisHour, bool suspicious)
    {
        // 12:30 -> (30 + 5 dk tolerans) x 250 = 8.750 adim siniri.
        await CreateService().SyncAsync(UserId, Request(Today, Hours((12, stepsThisHour))));

        var row = Assert.Single(_days);
        Assert.Equal(suspicious, row.IsSuspicious);
        if (suspicious)
        {
            Assert.Equal(StepActivityService.ReasonRateLimit, row.SuspiciousReason);
        }
    }

    [Fact]
    public async Task Sync_TimezoneMovesWestSameDay_DoesNotFlagEarlierLateHours()
    {
        // 12:30 (UTC+3) iken 12:00 kovasina adim yazildi; kullanici UTC+0'a uctu (yerel saat 09:30).
        await CreateService().SyncAsync(UserId, Request(Today, Hours((12, 3000))));
        var result = await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 400), (12, 3000)), offset: 0));

        Assert.True(Assert.Single(result.Days).IsVerified);
        Assert.Equal(3400, Assert.Single(_days).TotalSteps);
    }

    [Fact]
    public async Task Sync_DayRollsOverAtDeviceLocalMidnight()
    {
        // 21:30 UTC = 00:30 yerel (21 Kasim): yeni gun icin ayri kayit, dunku kayit degismez.
        await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 2000))));
        _clock.Now = new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero);

        await CreateService().SyncAsync(UserId, Request(Today.AddDays(1), Hours((0, 300))));

        Assert.Equal(2, _days.Count);
        Assert.Equal(2000, _days.Single(d => d.Date == Today).TotalSteps);
        Assert.Equal(300, _days.Single(d => d.Date == Today.AddDays(1)).TotalSteps);
    }

    [Fact]
    public async Task Sync_OnlyTouchesCallersRows()
    {
        _days.Add(new DailyActivity { Id = 1, UserId = OtherUserId, Date = Today, HourlySteps = Hours((9, 9000)), TotalSteps = 9000, DailyGoal = 10000, Source = "pedometer" });

        await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 100))));

        Assert.Equal(9000, _days.Single(d => d.UserId == OtherUserId).TotalSteps);
        Assert.Equal(100, _days.Single(d => d.UserId == UserId).TotalSteps);
        var otherView = await CreateService().GetDayAsync(UserId, Today);
        Assert.Equal(100, otherView.TotalSteps);
    }

    [Theory]
    [InlineData(23, 100, Offset)]
    [InlineData(24, -1, Offset)]
    [InlineData(24, 100, 900)]
    public async Task Sync_MalformedRequest_Throws(int hourCount, int value, int offset)
    {
        var hours = Enumerable.Repeat(value, hourCount).ToArray();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().SyncAsync(UserId, Request(Today, hours, offset)));
        Assert.Empty(_days);
    }

    [Fact]
    public async Task Sync_DuplicateDatesOrUnknownSource_Throws()
    {
        var duplicate = new StepSyncRequest
        {
            Source = "pedometer",
            Days = new List<StepSyncDayRequest>
            {
                new() { Date = Today, HourlySteps = new int[24], UtcOffsetMinutes = Offset },
                new() { Date = Today, HourlySteps = new int[24], UtcOffsetMinutes = Offset },
            },
        };
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().SyncAsync(UserId, duplicate));

        var badSource = Request(Today, new int[24]);
        badSource.Source = "manual";
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().SyncAsync(UserId, badSource));
    }

    [Fact]
    public async Task UpdateGoal_IsRejected_AndNothingIsWritten()
    {
        await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 6000))));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().UpdateGoalAsync(UserId, new UpdateStepGoalRequest { DailyGoal = 5000, Date = Today }));

        Assert.Contains("otomatik", ex.Message + string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
        Assert.Empty(_settings);
        Assert.Equal(10_000, Assert.Single(_days).DailyGoal);
    }

    [Fact]
    public async Task GetSettings_IgnoresOldUserChosenGoal()
    {
        _settings.Add(new UserActivitySetting { Id = 1, UserId = UserId, DailyStepGoal = 1000 });

        var settings = await CreateService().GetSettingsAsync(UserId);

        Assert.Equal(10_000, settings.DailyGoal);
        Assert.Equal("auto", settings.GoalSource);
    }

    [Fact]
    public async Task ProfileBasedGoal_IsAppliedToTodayOnNextSync_PastDaysKeepTheirGoal()
    {
        _days.Add(new DailyActivity { Id = 1, UserId = UserId, Date = Today.AddDays(-1), HourlySteps = Hours((9, 4000)), TotalSteps = 4000, DailyGoal = 10000, Source = "pedometer" });
        var profile = _profiles.Single();
        profile.ActivityLevel = ActivityLevel.Sedentary;
        profile.Goal = FitnessGoal.LoseWeight;

        var result = await CreateService().SyncAsync(UserId, Request(Today, Hours((9, 7000))));

        // Hareketsiz 6.000 + kilo verme 1.500 = 7.500
        var today = Assert.Single(result.Days);
        Assert.Equal(7_500, today.DailyGoal);
        Assert.False(today.GoalCompleted);
        Assert.Equal(10_000, _days.Single(d => d.Date == Today.AddDays(-1)).DailyGoal);
    }

    [Fact]
    public async Task GetSettings_DefaultGoalIsTenThousand()
    {
        var settings = await CreateService().GetSettingsAsync(OtherUserId);

        Assert.Equal(10_000, settings.DailyGoal);
        // Profil yok: varsayilan adim uzunlugu ve kilo.
        Assert.Equal(0.762, settings.StrideLengthMeters);
        Assert.Equal(Math.Round(0.762 / 1000 * 70 * 0.5, 5), settings.CaloriesPerStep);
    }

    [Fact]
    public async Task WeeklySummary_ComputesAverageMostActiveCompletionAndComparison()
    {
        void Add(int daysAgo, int steps, int goal = 10_000) => _days.Add(new DailyActivity
        {
            Id = _days.Count + 1, UserId = UserId, Date = Today.AddDays(-daysAgo), HourlySteps = Hours((10, steps)),
            TotalSteps = steps, DistanceMeters = steps, CaloriesBurned = steps / 25, DailyGoal = goal, Source = "pedometer",
        });
        Add(0, 4_000);
        Add(1, 12_000);
        Add(3, 10_000);
        Add(6, 2_000);
        Add(7, 9_000); // onceki donem
        Add(10, 5_000); // onceki donem

        var summary = await CreateService().GetWeeklySummaryAsync(UserId, Today);

        Assert.Equal(Today.AddDays(-6), summary.StartDate);
        Assert.Equal(7, summary.Days.Count);
        Assert.Equal(28_000, summary.TotalSteps);
        Assert.Equal(4_000, summary.AverageSteps);
        Assert.Equal(2, summary.GoalCompletedDays);
        Assert.Equal(28.6, summary.GoalCompletionRate);
        Assert.Equal(Today.AddDays(-1), summary.MostActiveDay!.Date);
        Assert.Equal(14_000, summary.PreviousPeriodTotalSteps);
        Assert.Equal(100, summary.ChangePercent);
        Assert.Equal(28_000, summary.TotalDistanceMeters);
    }

    [Fact]
    public async Task Range_FillsMissingDaysAndValidatesBounds()
    {
        _days.Add(new DailyActivity { Id = 1, UserId = UserId, Date = Today.AddDays(-1), HourlySteps = Hours((10, 500)), TotalSteps = 500, DailyGoal = 10000, Source = "pedometer" });

        var range = await CreateService().GetRangeAsync(UserId, Today.AddDays(-2), Today);

        Assert.Equal(new[] { 0, 500, 0 }, range.Days.Select(d => d.TotalSteps));
        Assert.Equal(new[] { false, true, false }, range.Days.Select(d => d.HasData));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().GetRangeAsync(UserId, Today, Today.AddDays(-1)));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().GetRangeAsync(UserId, Today.AddDays(-100), Today));
    }
}
