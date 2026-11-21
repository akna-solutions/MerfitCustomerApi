using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Energy;
using MerfitCustomerApi.Business.Services.Energy;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Energy;

/// <summary>
/// Servis: mevcut tablolardan (Meal/MealItem, DailyActivity, WorkoutSession, BodyMeasurement, NutritionGoal) okuma,
/// yerel gun siniri, gecmis tarihli kayitlar, kilo degisimi, kullanici izolasyonu ve aralik istatistikleri.
/// </summary>
public class EnergyBalanceServiceTests
{
    private const long UserId = 7;
    private const long OtherUserId = 8;

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly InMemoryUnitOfWork _uow = new();
    // 09:00 UTC = 12:00 Istanbul, 20 Kasim 2026.
    private readonly FakeTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero) };
    private static readonly DateOnly Today = new(2026, 11, 20);
    private long _nextId = 1;

    public EnergyBalanceServiceTests()
    {
        _uow.Register<Meal>().Register<MealItem>().Register<DailyActivity>().Register<WorkoutSession>()
            .Register<Workout>().Register<WorkoutCategory>().Register<WorkoutAttributeScore>()
            .Register<BodyMeasurement>().Register<NutritionGoal>();
        _uow.Set<UserPreference>().Add(new UserPreference { UserId = UserId, TimeZone = "Europe/Istanbul" });
        // 1996-05-01 dogumlu -> 30 yas; erkek 80 kg 180 cm -> BMR 1780.
        _uow.Set<UserProfile>().Add(new UserProfile
        {
            UserId = UserId, Gender = Gender.Male, WeightKg = 80, HeightCm = 180, DateOfBirth = new DateTime(1996, 5, 1),
            Goal = FitnessGoal.LoseWeight, ActivityLevel = ActivityLevel.LightlyActive,
        });
        _uow.Set<NutritionGoal>().Add(new NutritionGoal { UserId = UserId, DailyCalories = 2000, ProteinTarget = 150, CarbsTarget = 200, FatTarget = 60 });
        _uow.Set<Workout>().Add(new Workout { Id = 1, Title = "Tempolu Yürüyüş", CategoryId = 1, Difficulty = DifficultyLevel.Intermediate });
        _uow.Set<WorkoutCategory>().Add(new WorkoutCategory { Id = 1, Name = "Yürüyüş", Slug = "yuruyus" });
    }

    private EnergyBalanceService CreateService() => new(
        _uow.Object,
        new NutritionCalculator(Options.Create(new NutritionCalculationOptions())),
        Options.Create(new EnergyBalanceOptions()),
        Options.Create(new NutritionCalculationOptions()),
        clock: _clock);

    private void AddMeal(DateOnly date, MealType type, decimal kcal, decimal protein = 40, long userId = UserId)
    {
        var meal = new Meal { Id = _nextId++, UserId = userId, Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), MealType = type };
        _uow.Set<Meal>().Add(meal);
        _uow.Set<MealItem>().Add(new MealItem { Id = _nextId++, MealId = meal.Id, Calories = kcal, Protein = protein, Carbs = 50, Fat = 20 });
    }

    private void AddSteps(DateOnly date, int[] hourly, DateTime? syncedAt = null)
    {
        var total = hourly.Sum();
        _uow.Set<DailyActivity>().Add(new DailyActivity
        {
            Id = _nextId++, UserId = UserId, Date = date, HourlySteps = hourly, TotalSteps = total,
            DistanceMeters = (int)Math.Round(total * 0.75), CaloriesBurned = (int)Math.Round(total * 0.04),
            UtcOffsetMinutes = 180, Source = "pedometer", LastSyncedAt = syncedAt ?? _clock.Now.UtcDateTime,
        });
    }

    private void AddWorkout(DateTime completedAtUtc, int durationSeconds, decimal? calories, WorkoutSessionStatus status = WorkoutSessionStatus.Completed)
    {
        _uow.Set<WorkoutSession>().Add(new WorkoutSession
        {
            Id = _nextId++, UserId = UserId, WorkoutId = 1, StartedAt = completedAtUtc.AddSeconds(-durationSeconds),
            CompletedAt = completedAtUtc, DurationSeconds = durationSeconds, CaloriesBurned = calories, Status = status,
        });
    }

    private static int[] Hours(params (int Hour, int Steps)[] values)
    {
        var hours = new int[24];
        foreach (var (hour, steps) in values)
        {
            hours[hour] = steps;
        }

        return hours;
    }

    [Fact]
    public async Task Today_DefaultsToUsersLocalDay_AndProratesBmr()
    {
        AddMeal(Today, MealType.Breakfast, 500);

        var result = await CreateService().GetDayAsync(UserId, null);

        Assert.Equal(Today, result.Date);
        Assert.True(result.IsToday);
        Assert.Equal(0.5, result.ElapsedDayFraction);
        Assert.Equal(890, result.Expenditure.Bmr);
        Assert.Equal(500, result.IntakeCalories);
        Assert.Equal(1500, result.RemainingCalories);
        Assert.Equal(2000, result.TargetCalories);
        Assert.Equal("lose", result.GoalDirection);
        Assert.Equal(EnergyStatuses.InProgress, result.Status);
    }

    [Fact]
    public async Task FutureDate_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().GetDayAsync(UserId, Today.AddDays(1)));
    }

    // 8. Gun degisimi: UTC'de 19 Kasim 22:30 biten antrenman Istanbul'da 20 Kasim 01:30'dur.
    [Fact]
    public async Task Workout_IsAssignedToLocalDay_NotUtcDay()
    {
        AddWorkout(new DateTime(2026, 11, 19, 22, 30, 0, DateTimeKind.Utc), 1800, 120);

        var yesterday = await CreateService().GetDayAsync(UserId, Today.AddDays(-1));
        var today = await CreateService().GetDayAsync(UserId, Today);

        Assert.Equal(0, yesterday.Expenditure.Workout);
        Assert.Equal(120, today.Expenditure.Workout);
    }

    // 9. Gecmis tarihli besin eklenmesi: hicbir sey onbelleklenmez, sonraki istek guncel sonucu verir.
    [Fact]
    public async Task BackdatedMeal_IsReflectedOnThatDay()
    {
        var day = Today.AddDays(-3);
        var before = await CreateService().GetDayAsync(UserId, day);
        AddMeal(day, MealType.Dinner, 700);
        var after = await CreateService().GetDayAsync(UserId, day);

        Assert.Equal(0, before.IntakeCalories);
        Assert.Equal(700, after.IntakeCalories);
        Assert.Equal(700, after.Nutrition.Meals.Single(m => m.Type == "dinner").Calories);
        Assert.Equal(_uow.Object.Repository<Meal>().GetQueryable().Count(), 1); // hicbir sey yazilmadi
    }

    // 10. Gecmis tarihli antrenman eklenmesi + yuruyus adimlariyla cakisma
    [Fact]
    public async Task BackdatedWalkingWorkout_CountsOnce_WithStepsOfThatDay()
    {
        var day = Today.AddDays(-2);
        AddSteps(day, Hours((8, 3000), (18, 3000)));
        // 08:00-08:30 yerel = 05:00-05:30 UTC
        AddWorkout(day.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Utc), 1800, 150);

        var result = await CreateService().GetDayAsync(UserId, day);

        Assert.Equal(1500, result.Activity.OverlappingSteps);
        Assert.Equal(150, result.Expenditure.Workout);
        Assert.Equal(120 + 60, result.Expenditure.Walking + result.Expenditure.DailyMovement);
        Assert.Equal("walking", Assert.Single(result.Activity.Workouts).Kind);
    }

    [Fact]
    public async Task WorkoutWithoutStoredCalories_IsEstimatedWithSameFormula()
    {
        var day = Today.AddDays(-1);
        AddWorkout(day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc), 3600, null);

        var result = await CreateService().GetDayAsync(UserId, day);

        // Yuruyus, orta: MET 4,3 -> (4,3 - 1) x 80 x 1 = 264
        Assert.Equal(264, result.Expenditure.Workout);
    }

    [Fact]
    public async Task AbandonedWorkout_IsIgnored()
    {
        AddWorkout(Today.AddDays(-1).ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc), 3600, 300, WorkoutSessionStatus.Abandoned);

        var result = await CreateService().GetDayAsync(UserId, Today.AddDays(-1));

        Assert.Equal(0, result.Expenditure.Workout);
    }

    // 7. Kilo degisimi: her gun o gunun (ya da onceki en yakin) tartimini kullanir.
    [Fact]
    public async Task WeightChange_EachDayUsesItsOwnWeighIn()
    {
        _uow.Set<BodyMeasurement>().Add(new BodyMeasurement { Id = 1, UserId = UserId, RecordedAt = Today.AddDays(-10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), WeightKg = 82 });
        _uow.Set<BodyMeasurement>().Add(new BodyMeasurement { Id = 2, UserId = UserId, RecordedAt = Today.AddDays(-3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), WeightKg = 79 });

        var range = await CreateService().GetRangeAsync(UserId, Today.AddDays(-5), Today.AddDays(-1));
        var early = await CreateService().GetDayAsync(UserId, Today.AddDays(-5));
        var late = await CreateService().GetDayAsync(UserId, Today.AddDays(-2));

        Assert.Equal(1800, early.Expenditure.Bmr); // 82 kg
        Assert.Equal(1770, late.Expenditure.Bmr);  // 79 kg
        Assert.True(late.DataQuality.WeightFromMeasurement);
        Assert.Equal(82, range.Summary.Weight.StartKg);
        Assert.Equal(79, range.Summary.Weight.EndKg);
        Assert.Equal(-3, range.Summary.Weight.ChangeKg);
    }

    [Fact]
    public async Task OtherUsersData_IsNeverIncluded()
    {
        AddMeal(Today.AddDays(-1), MealType.Lunch, 900, userId: OtherUserId);

        var result = await CreateService().GetDayAsync(UserId, Today.AddDays(-1));

        Assert.Equal(0, result.IntakeCalories);
    }

    [Fact]
    public async Task Range_AveragesOnlyCompleteDays_AndBuildsWeeks()
    {
        // 14 gun: yalnizca 4 gunde besin kaydi var.
        foreach (var offset in new[] { 1, 2, 3, 9 })
        {
            AddMeal(Today.AddDays(-offset), MealType.Lunch, 1600);
        }

        var result = await CreateService().GetRangeAsync(UserId, Today.AddDays(-13), Today);

        Assert.Equal(14, result.Days.Count);
        Assert.Equal(4, result.Summary.CompleteDays);
        // Her tam gun: 1600 - (1780 + 160) = -340
        Assert.Equal(-340, result.Summary.AverageBalance);
        Assert.Equal(-1360, result.Summary.TotalBalance);
        Assert.Equal(-0.18m, result.Summary.EstimatedWeightChangeKg);
        Assert.Equal(2, result.Weeks.Count);
        Assert.Equal(Today, result.Weeks[^1].End);
        Assert.Equal(3, result.Weeks[^1].CompleteDays); // bugun tamamlanmadigi icin sayilmaz
        Assert.Equal(1, result.Weeks[0].CompleteDays);
        Assert.NotNull(result.Summary.Analysis);
        Assert.True(result.Days.Single(d => d.Date == Today).IsToday);
        Assert.False(result.Days.Single(d => d.Date == Today).IsComplete);
    }

    [Fact]
    public async Task Range_TooLong_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().GetRangeAsync(UserId, Today.AddDays(-100), Today));
    }

    [Fact]
    public async Task Range_EndInFuture_IsClampedToToday()
    {
        var result = await CreateService().GetRangeAsync(UserId, Today.AddDays(-6), Today.AddDays(3));

        Assert.Equal(Today, result.To);
        Assert.Equal(7, result.Days.Count);
    }

    // 11. Profil eksik: harcama hesaplanmaz, alinan yine gosterilir
    [Fact]
    public async Task MissingProfile_ReturnsIntakeOnly()
    {
        _uow.Set<UserProfile>().Single().HeightCm = null;
        AddMeal(Today.AddDays(-1), MealType.Lunch, 900);

        var result = await CreateService().GetDayAsync(UserId, Today.AddDays(-1));

        Assert.Equal(900, result.IntakeCalories);
        Assert.Null(result.ExpenditureCalories);
        Assert.Contains("height", result.DataQuality.MissingProfileFields);
        Assert.Equal(EnergyStatuses.InsufficientData, result.Status);
    }

    // 12. Senkronizasyon hatasi
    [Fact]
    public async Task StaleSync_IsReportedForToday()
    {
        AddSteps(Today, Hours((8, 2500)), syncedAt: _clock.Now.UtcDateTime.AddHours(-4));

        var result = await CreateService().GetDayAsync(UserId, null);

        Assert.True(result.DataQuality.StepSyncStale);
        Assert.Contains(result.Insights, i => i.Code == "step_sync_stale");
    }

    [Theory]
    [InlineData(FitnessGoal.LoseWeight, null, "lose")]
    [InlineData(FitnessGoal.BuildMuscle, null, "gain")]
    [InlineData(FitnessGoal.GetStronger, null, "maintain")]
    [InlineData(FitnessGoal.BuildMuscle, 0, "maintain")]
    [InlineData(FitnessGoal.LoseWeight, -400, "lose")]
    public void ResolveDirection_FollowsCalculatedAdjustment_ThenGoal(FitnessGoal goal, int? adjustment, string expected)
    {
        Assert.Equal(expected, EnergyBalanceService.ResolveDirection(goal, adjustment is null ? null : (decimal)adjustment.Value));
    }
}
