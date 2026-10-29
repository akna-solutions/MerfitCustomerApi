using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Calendar;
using MerfitCustomerApi.Business.Tests.AccountEmail;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Calendar;

/// <summary>
/// Ilerleme takvimi / haftalik aktivite: gun sinirlari (saat dilimi), alinan ve harcanan kalorinin ayrimi,
/// beslenme hedefinin tamamlanma kurali, gelecek gunler ve program gunleri.
/// </summary>
public class ActivityCalendarServiceTests
{
    private const long UserId = 11;
    private const long OtherUserId = 12;

    private readonly InMemoryUnitOfWork _db = new();
    // 2026-10-20 09:00 UTC = 12:00 Istanbul (Sali).
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };

    public ActivityCalendarServiceTests()
    {
        _db.Register<UserPreference>().Register<WorkoutSession>().Register<Workout>().Register<WorkoutSetLog>()
            .Register<WorkoutSessionExercise>().Register<Meal>().Register<MealItem>().Register<NutritionGoal>()
            .Register<WorkoutPlan>().Register<WorkoutPlanDay>();
        _db.Set<UserPreference>().Add(new UserPreference { Id = 1, UserId = UserId, Language = "tr", TimeZone = "Europe/Istanbul" });
        _db.Set<Workout>().Add(new Workout { Id = 100, Title = "Üst Vücut" });
    }

    private ActivityCalendarService CreateService() =>
        new(_db.Object, Options.Create(new NotificationOptions()), _clock);

    private static DateOnly D(int day) => new(2026, 10, day);

    private void AddSession(long id, long userId, DateTime completedAtUtc, int durationSeconds = 1800, decimal? calories = 250,
        WorkoutSessionStatus status = WorkoutSessionStatus.Completed)
    {
        _db.Set<WorkoutSession>().Add(new WorkoutSession
        {
            Id = id, UserId = userId, WorkoutId = 100, Status = status, StartedAt = completedAtUtc.AddMinutes(-30),
            CompletedAt = completedAtUtc, DurationSeconds = durationSeconds, CaloriesBurned = calories,
        });
    }

    private void AddMeal(long id, long userId, DateOnly date, MealType type, decimal calories, decimal protein = 10)
    {
        _db.Set<Meal>().Add(new Meal { Id = id, UserId = userId, Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), MealType = type });
        _db.Set<MealItem>().Add(new MealItem { Id = id * 10, MealId = id, Calories = calories, Protein = protein, Carbs = 20, Fat = 5 });
    }

    [Fact]
    public async Task Workout_IsAssignedToUsersLocalDay()
    {
        // 14 Ekim 22:30 UTC = 15 Ekim 01:30 Istanbul: antrenman 15'inde gorunmeli.
        AddSession(1, UserId, new DateTime(2026, 10, 14, 22, 30, 0, DateTimeKind.Utc));

        var result = await CreateService().GetCalendarAsync(UserId, D(14), D(15));

        Assert.False(result.Days[0].Workout.Completed);
        var day = result.Days[1];
        Assert.True(day.Workout.Completed);
        var session = Assert.Single(day.Workout.Sessions);
        Assert.Equal("Üst Vücut", session.Title);
        Assert.Equal(250, day.Activity.CaloriesBurned);
        Assert.Equal(30, day.Activity.ActiveMinutes);
        Assert.Equal("Europe/Istanbul", result.TimeZone);
    }

    [Fact]
    public async Task OnlyOwnCompletedSessions_AreCounted()
    {
        AddSession(1, OtherUserId, new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc));
        AddSession(2, UserId, new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), status: WorkoutSessionStatus.Cancelled);

        var result = await CreateService().GetCalendarAsync(UserId, D(15), D(15));

        Assert.False(Assert.Single(result.Days).Workout.Completed);
    }

    [Fact]
    public async Task SetAndExerciseCounts_AreAggregatedPerSession()
    {
        AddSession(1, UserId, new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc));
        _db.Set<WorkoutSessionExercise>().AddRange(
            new WorkoutSessionExercise { Id = 1, WorkoutSessionId = 1, ExerciseId = 5 },
            new WorkoutSessionExercise { Id = 2, WorkoutSessionId = 1, ExerciseId = 6 },
            new WorkoutSessionExercise { Id = 3, WorkoutSessionId = 1, ExerciseId = 7 });
        _db.Set<WorkoutSetLog>().AddRange(
            new WorkoutSetLog { Id = 1, WorkoutSessionExerciseId = 1, SetNumber = 1 },
            new WorkoutSetLog { Id = 2, WorkoutSessionExerciseId = 1, SetNumber = 2 },
            new WorkoutSetLog { Id = 3, WorkoutSessionExerciseId = 2, SetNumber = 1 });

        var result = await CreateService().GetCalendarAsync(UserId, D(15), D(15));

        var session = Assert.Single(result.Days[0].Workout.Sessions);
        Assert.Equal(3, session.SetCount);
        Assert.Equal(2, session.ExerciseCount); // seti olmayan egzersiz sayilmaz
    }

    [Fact]
    public async Task Nutrition_ConsumedCaloriesAreSeparateFromBurned_AndGoalUsesInsightThresholds()
    {
        _db.Set<NutritionGoal>().Add(new NutritionGoal { Id = 1, UserId = UserId, DailyCalories = 2000 });
        AddMeal(1, UserId, D(15), MealType.Breakfast, 900);
        AddMeal(2, UserId, D(15), MealType.Dinner, 1000);   // 1900 / 2000 = %95 -> tamamlandi
        AddMeal(3, UserId, D(16), MealType.Lunch, 1000);    // %50 -> tamamlanmadi
        AddMeal(4, UserId, D(17), MealType.Lunch, 2400);    // %120 -> asildi, tamamlanmadi
        AddMeal(5, OtherUserId, D(18), MealType.Lunch, 2000);
        AddSession(1, UserId, new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), calories: 400);

        var result = await CreateService().GetCalendarAsync(UserId, D(15), D(18));

        Assert.Equal(2000, result.DailyCalorieTarget);
        var d15 = result.Days[0];
        Assert.Equal(1900, d15.Nutrition.ConsumedCalories);
        Assert.Equal(400, d15.Activity.CaloriesBurned);
        Assert.True(d15.Nutrition.CalorieGoalMet);
        Assert.Equal(2, d15.Nutrition.Meals.Count);
        Assert.Equal("Kahvaltı", d15.Nutrition.Meals[0].Type);
        Assert.False(result.Days[1].Nutrition.CalorieGoalMet);
        Assert.False(result.Days[2].Nutrition.CalorieGoalMet);
        Assert.False(result.Days[3].Nutrition.Logged);
        Assert.Null(result.Days[3].Nutrition.CalorieRatio);
    }

    [Fact]
    public async Task WithoutGoal_NoDayIsMarkedComplete()
    {
        AddMeal(1, UserId, D(15), MealType.Lunch, 2000);

        var result = await CreateService().GetCalendarAsync(UserId, D(15), D(15));

        Assert.Null(result.DailyCalorieTarget);
        Assert.True(result.Days[0].Nutrition.Logged);
        Assert.False(result.Days[0].Nutrition.CalorieGoalMet);
    }

    [Fact]
    public async Task FutureDays_HaveNoActivity()
    {
        // Hatali tarihli veri olsa bile gelecek gun tamamlanmis gorunmez.
        AddMeal(1, UserId, D(22), MealType.Lunch, 1000);

        var result = await CreateService().GetCalendarAsync(UserId, D(19), D(22));

        Assert.Equal("2026-10-20", result.Today);
        Assert.True(result.Days[1].IsToday);
        Assert.False(result.Days[1].IsFuture);
        Assert.True(result.Days[3].IsFuture);
        Assert.False(result.Days[3].Nutrition.Logged);
    }

    [Fact]
    public async Task PlannedWorkoutDays_ComeFromActivePlan_AndAreUnknownBeforePlanStart()
    {
        _db.Set<WorkoutPlan>().Add(new WorkoutPlan { Id = 1, UserId = UserId, IsActive = true, StartDate = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc) });
        _db.Set<WorkoutPlanDay>().AddRange(
            new WorkoutPlanDay { Id = 1, WorkoutPlanId = 1, DayOfWeek = DayOfWeek.Monday, WorkoutId = 100 },
            new WorkoutPlanDay { Id = 2, WorkoutPlanId = 1, DayOfWeek = DayOfWeek.Thursday, WorkoutId = 100 });

        // 12 Ekim Pazartesi (plan oncesi), 19 Ekim Pazartesi, 20 Ekim Sali.
        var result = await CreateService().GetCalendarAsync(UserId, D(12), D(20));

        Assert.Null(result.Days[0].IsPlannedWorkoutDay);
        Assert.True(result.Days.Single(d => d.Date == "2026-10-19").IsPlannedWorkoutDay);
        Assert.False(result.Days.Single(d => d.Date == "2026-10-20").IsPlannedWorkoutDay);
        Assert.True(result.Days.Single(d => d.Date == "2026-10-15").IsPlannedWorkoutDay);
    }

    [Fact]
    public async Task WithoutPlan_PlannedDayIsUnknown()
    {
        var result = await CreateService().GetCalendarAsync(UserId, D(19), D(20));

        Assert.All(result.Days, d => Assert.Null(d.IsPlannedWorkoutDay));
    }

    [Fact]
    public async Task MissingPreference_FallsBackToDefaultTimeZone()
    {
        _db.Set<UserPreference>().Clear();

        var result = await CreateService().GetCalendarAsync(UserId, D(1), D(31));

        Assert.Equal(31, result.Days.Count);
        Assert.Equal("Europe/Istanbul", result.TimeZone);
    }

    [Theory]
    [InlineData(2026, 10, 20, 2026, 10, 19)]   // to < from
    [InlineData(2026, 1, 1, 2026, 3, 31)]      // 62 gunden uzun
    [InlineData(1, 1, 1, 1, 1, 1)]             // sorgu parametresi verilmedi (DateOnly varsayilani)
    public async Task InvalidRange_IsRejected(int fy, int fm, int fd, int ty, int tm, int td)
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().GetCalendarAsync(UserId, new DateOnly(fy, fm, fd), new DateOnly(ty, tm, td)));
    }
}
