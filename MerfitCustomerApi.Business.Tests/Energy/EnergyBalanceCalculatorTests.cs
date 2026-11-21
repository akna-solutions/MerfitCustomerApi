using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Energy;
using MerfitCustomerApi.Business.Services.Energy;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Energy;

/// <summary>
/// Saf kalori dengesi hesabi: alinan/harcanan, BMR (Mifflin-St Jeor), adim ve antrenman cifte sayiminin onlenmesi,
/// hedefe gore renk/yorum ve eksik veri davranisi.
/// Referans kullanici: erkek, 80 kg, 180 cm, 30 yas -> BMR = 800 + 1125 - 150 + 5 = 1780 kcal.
/// </summary>
public class EnergyBalanceCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 11, 18);
    /// <summary>Turkiye (UTC+3).</summary>
    private const int Offset = 180;
    private const int Bmr = 1780;

    private static EnergyBalanceCalculator Calculator() => new(new EnergyBalanceOptions(), new NutritionCalculationOptions());

    private static EnergyDayInput Input(
        IReadOnlyList<EnergyMealEntry>? meals = null,
        EnergyStepDay? steps = null,
        IReadOnlyList<EnergyWorkoutEntry>? workouts = null,
        string direction = "lose",
        int? target = 2200,
        int? planned = -500,
        int? proteinTarget = 150,
        decimal? weight = 80,
        decimal? height = 180,
        int? age = 30,
        Gender? gender = Gender.Male,
        bool isToday = false,
        double fraction = 1,
        DateTime? nowUtc = null) => new()
    {
        Date = Day,
        IsToday = isToday,
        ElapsedDayFraction = fraction,
        NowUtc = nowUtc ?? Day.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc),
        WeightKg = weight,
        HeightCm = height,
        Age = age,
        Gender = gender,
        Meals = meals ?? Array.Empty<EnergyMealEntry>(),
        Steps = steps,
        Workouts = workouts ?? Array.Empty<EnergyWorkoutEntry>(),
        TargetCalories = target,
        ProteinTarget = proteinTarget,
        CarbsTarget = 250,
        FatTarget = 70,
        PlannedBalance = planned,
        GoalDirection = direction,
    };

    private static EnergyMealEntry Meal(MealType type, decimal kcal, decimal protein = 30, decimal carbs = 50, decimal fat = 20) =>
        new(type, kcal, protein, carbs, fat);

    private static int[] Hours(params (int Hour, int Steps)[] values)
    {
        var hours = new int[24];
        foreach (var (hour, steps) in values)
        {
            hours[hour] = steps;
        }

        return hours;
    }

    /// <summary>Adim kalorisi sunucuda kaydedilmis gibi: 0,04 kcal/adim, 0,75 m/adim.</summary>
    private static EnergyStepDay Steps(int[] hours, bool suspicious = false, DateTime? syncedAt = null)
    {
        var total = hours.Sum();
        return new EnergyStepDay(total, (int)Math.Round(total * 0.75), (int)Math.Round(total * 0.04), hours, Offset, suspicious,
            syncedAt ?? Day.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc));
    }

    /// <summary>Yerel saatte (UTC+3) biten antrenman.</summary>
    private static EnergyWorkoutEntry Workout(int localEndHour, int localEndMinute, int minutes, int? kcal, WorkoutEnergyKind kind = WorkoutEnergyKind.Strength, long id = 1) =>
        new(id, "Antrenman", kind, Day.ToDateTime(new TimeOnly(localEndHour, localEndMinute), DateTimeKind.Utc).AddMinutes(-Offset), minutes * 60, kcal);

    // 1. Hic yemek kaydi olmayan kullanici
    [Fact]
    public void NoMeals_ComputesExpenditure_ButDoesNotJudgeBalance()
    {
        var result = Calculator().Calculate(Input());

        Assert.Equal(0, result.IntakeCalories);
        Assert.Equal(Bmr, result.Expenditure.Bmr);
        Assert.Equal(Bmr, result.ExpenditureCalories);
        Assert.Equal(EnergyStatuses.InsufficientData, result.Status);
        Assert.Equal(EnergyTones.Blue, result.Tone);
        Assert.False(result.DataQuality.HasMeals);
        Assert.False(result.DataQuality.IsComplete);
        Assert.DoesNotContain(result.Insights, i => i.Code is "balance_aligned" or "deficit_reached" or "protein_low");
    }

    // 2. Sadece yemek kaydi olan kullanici
    [Fact]
    public void OnlyMeals_ExpenditureIsBmrPlusThermicEffect()
    {
        var result = Calculator().Calculate(Input(meals: new[]
        {
            Meal(MealType.Breakfast, 500), Meal(MealType.Lunch, 700), Meal(MealType.Dinner, 600), Meal(MealType.Snack, 200),
        }));

        Assert.Equal(2000, result.IntakeCalories);
        Assert.Equal(200, result.Expenditure.ThermicEffect);
        Assert.Equal(Bmr + 200, result.ExpenditureCalories);
        Assert.Equal(20, result.Balance);
        Assert.Equal(200, result.RemainingCalories);
        Assert.Equal(EnergyStatuses.Neutral, result.Status); // dengeye yakin, kilo verme icin acik yok
        Assert.Equal(new[] { "breakfast", "lunch", "dinner", "snack" }, result.Nutrition.Meals.Select(m => m.Type));
        Assert.Equal(new[] { 500, 700, 600, 200 }, result.Nutrition.Meals.Select(m => m.Calories));
        Assert.Equal(120, result.Nutrition.Protein);
        Assert.Equal(200, result.Nutrition.Carbs);
        Assert.Equal(80, result.Nutrition.Fat);
    }

    // 3. Sadece yuruyus yapan kullanici
    [Fact]
    public void OnlyWalking_SplitsStepCaloriesIntoWalkingAndDailyMovement_WithoutChangingTotal()
    {
        var steps = Steps(Hours((8, 3000), (12, 1200), (15, 800), (18, 4000)));

        var result = Calculator().Calculate(Input(steps: steps));

        Assert.Equal(9000, result.Activity.Steps);
        Assert.Equal(7000, result.Activity.WalkingSteps);
        Assert.Equal(280, result.Expenditure.Walking);        // 7.000 x 0,04
        Assert.Equal(80, result.Expenditure.DailyMovement);   // 2.000 x 0,04
        Assert.Equal(steps.CaloriesBurned, result.Expenditure.Walking + result.Expenditure.DailyMovement);
        Assert.Equal(0, result.Expenditure.DeduplicatedStepCalories);
        Assert.Equal(Bmr + 360, result.ExpenditureCalories);
        Assert.Contains(result.Insights, i => i.Code == "walking_contribution");
    }

    // 4. Antrenman ve yuruyus yapan kullanici (cakisma yok)
    [Fact]
    public void WorkoutAndWalking_NoOverlap_AddsBoth()
    {
        var steps = Steps(Hours((8, 3000), (18, 3000)));
        var result = Calculator().Calculate(Input(steps: steps, workouts: new[] { Workout(21, 0, 60, 320) }));

        Assert.Equal(0, result.Activity.OverlappingSteps);
        Assert.Equal(240, result.Expenditure.Walking);
        Assert.Equal(320, result.Expenditure.Workout);
        Assert.Equal(Bmr + 240 + 320, result.ExpenditureCalories);
        Assert.Equal(60, result.Activity.WorkoutMinutes);
    }

    // 5. Hem yemek hem antrenman kaydi olan kullanici
    [Fact]
    public void MealsAndWorkout_LoseGoal_DeficitIsGreen_AndReachedPlannedDeficit()
    {
        var result = Calculator().Calculate(Input(
            meals: new[] { Meal(MealType.Lunch, 900, protein: 80), Meal(MealType.Dinner, 900, protein: 80) },
            workouts: new[] { Workout(19, 0, 60, 400) },
            planned: -500));

        // 1780 + 180 (termik) + 400 = 2360; 1800 - 2360 = -560
        Assert.Equal(2360, result.ExpenditureCalories);
        Assert.Equal(-560, result.Balance);
        Assert.Equal("deficit", result.BalanceKind);
        Assert.Equal(EnergyStatuses.Aligned, result.Status);
        Assert.Equal(EnergyTones.Green, result.Tone);
        Assert.True(result.DataQuality.IsComplete);
        Assert.Contains(result.Insights, i => i.Code == "deficit_reached");
        Assert.Contains(result.Insights, i => i.Code == "workout_contribution");
    }

    // 6. Ayni aktivitenin iki kez sayilmasi: yuruyus antrenmani sirasinda sayilan adimlar
    [Fact]
    public void WalkingWorkout_OverlappingSteps_AreNotCountedTwice()
    {
        // 08:00-08:30 yerel yuruyus antrenmani; 08 saatinde 3.000 adim -> yarisi (1.500) antrenmanla cakisir.
        var steps = Steps(Hours((8, 3000), (18, 3000)));
        var workout = Workout(8, 30, 30, 150, WorkoutEnergyKind.Walking);

        var result = Calculator().Calculate(Input(steps: steps, workouts: new[] { workout }));

        Assert.Equal(1500, result.Activity.OverlappingSteps);
        Assert.Equal(60, result.Expenditure.DeduplicatedStepCalories); // 1.500 x 0,04
        Assert.Equal(120, result.Expenditure.Walking);                 // 18 saati (3.000 adim)
        Assert.Equal(60, result.Expenditure.DailyMovement);            // 08 saatinden kalan 1.500 adim (esik alti)
        Assert.Equal(150, result.Expenditure.Workout);
        Assert.Equal(Bmr + 120 + 60 + 150, result.ExpenditureCalories);
        Assert.Contains(result.Insights, i => i.Code == "steps_deduplicated");
    }

    [Fact]
    public void OverlappingWorkouts_NeverRemoveMoreStepsThanTheHourHad()
    {
        var steps = Steps(Hours((8, 2000)));
        var workouts = new[] { Workout(9, 0, 60, 100, id: 1), Workout(9, 0, 60, 100, id: 2) };

        var result = Calculator().Calculate(Input(steps: steps, workouts: workouts));

        Assert.Equal(2000, result.Activity.OverlappingSteps);
        Assert.Equal(0, result.Expenditure.Walking + result.Expenditure.DailyMovement);
        Assert.Equal(80, result.Expenditure.DeduplicatedStepCalories);
    }

    [Fact]
    public void ExpenditureComponents_AlwaysSumToTotal()
    {
        var result = Calculator().Calculate(Input(
            meals: new[] { Meal(MealType.Breakfast, 433) },
            steps: Steps(Hours((7, 2600), (8, 2777), (19, 1333))),
            workouts: new[] { Workout(8, 20, 40, 211, WorkoutEnergyKind.Running) }));

        var e = result.Expenditure;
        Assert.Equal(result.ExpenditureCalories, e.Bmr + e.Walking + e.DailyMovement + e.Workout + e.ThermicEffect);
        Assert.Equal(Steps(Hours((7, 2600), (8, 2777), (19, 1333))).CaloriesBurned, e.Walking + e.DailyMovement + e.DeduplicatedStepCalories);
        Assert.Equal("bmr_plus_active", e.Method);
    }

    // 7. Kullanicinin kilosunun degismesi: BMR o gunun kilosuyla hesaplanir
    [Fact]
    public void WeightChange_UsesWeightOfThatDay()
    {
        var heavier = Calculator().Calculate(Input(weight: 80));
        var lighter = Calculator().Calculate(Input(weight: 75));

        Assert.Equal(Bmr, heavier.Expenditure.Bmr);
        Assert.Equal(Bmr - 50, lighter.Expenditure.Bmr);
    }

    // 8. Gun degisimi: bugun icin BMR gunun gecen kismi kadardir, yargi yapilmaz
    [Fact]
    public void Today_ProratesBmr_AndStaysInProgress()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Breakfast, 600) }, isToday: true, fraction: 0.5));

        Assert.Equal(890, result.Expenditure.Bmr);
        Assert.Equal(Bmr, result.Expenditure.BmrFullDay);
        Assert.Equal(EnergyStatuses.InProgress, result.Status);
        Assert.Equal(EnergyTones.Blue, result.Tone);
        Assert.Equal(1600, result.RemainingCalories);
        Assert.True(result.DataQuality.IsPartialDay);
        Assert.False(result.DataQuality.IsComplete);
        Assert.DoesNotContain(result.Insights, i => i.Code == "protein_low"); // gunun yarisinda protein yargisi yok
    }

    [Fact]
    public void Today_OverTarget_IsOrange()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Dinner, 2600) }, isToday: true, fraction: 0.8));

        Assert.Equal(EnergyStatuses.Surplus, result.Status);
        Assert.Equal(EnergyTones.Orange, result.Tone);
        Assert.Equal(-400, result.RemainingCalories);
    }

    // 11. Veri eksikligi
    [Fact]
    public void MissingProfileData_NoBmr_NoBalance_ListsMissingFields()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Lunch, 800) }, height: null, age: null));

        Assert.Null(result.Expenditure.Bmr);
        Assert.Null(result.ExpenditureCalories);
        Assert.Null(result.Balance);
        Assert.Equal(EnergyStatuses.InsufficientData, result.Status);
        Assert.Equal(new[] { "height", "birthDate" }, result.DataQuality.MissingProfileFields);
        Assert.Contains(result.Insights, i => i.Code == "profile_incomplete");
        Assert.Equal(800, result.IntakeCalories); // alinan kalori yine gosterilir
    }

    [Fact]
    public void UnknownGender_UsesAverageConstant_AndFlagsIt()
    {
        var result = Calculator().Calculate(Input(gender: null));

        Assert.Equal(Bmr - 5 - 78, result.Expenditure.Bmr);
        Assert.True(result.DataQuality.GenderEstimated);
    }

    [Fact]
    public void WorkoutWithoutCalories_IsNotGuessed_AndFlagged()
    {
        var result = Calculator().Calculate(Input(workouts: new[] { Workout(10, 0, 30, null) }));

        Assert.Equal(0, result.Expenditure.Workout);
        Assert.Equal(1, result.DataQuality.WorkoutsWithoutCalories);
        Assert.Contains(result.Insights, i => i.Code == "workout_without_calories");
    }

    // 12. Senkronizasyon hatasi: bugunun adimlari uzun suredir gelmiyor
    [Fact]
    public void StaleStepSync_Today_IsFlagged()
    {
        var now = Day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);
        var result = Calculator().Calculate(Input(
            steps: Steps(Hours((8, 2000)), syncedAt: now.AddHours(-5)), isToday: true, fraction: 0.75, nowUtc: now));

        Assert.True(result.DataQuality.StepSyncStale);
        Assert.Contains(result.Insights, i => i.Code == "step_sync_stale");
    }

    [Fact]
    public void SuspiciousSteps_AreCounted_ButFlaggedAsUncertain()
    {
        var result = Calculator().Calculate(Input(steps: Steps(Hours((8, 3000)), suspicious: true)));

        Assert.False(result.DataQuality.StepsVerified);
        Assert.Contains(result.Insights, i => i.Code == "steps_unverified");
    }

    [Fact]
    public void LoseGoal_ExcessiveDeficit_IsRed()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Lunch, 1300) }, workouts: new[] { Workout(19, 0, 90, 700) }));

        // 1780 + 130 + 700 = 2610; 1300 - 2610 = -1310
        Assert.Equal(-1310, result.Balance);
        Assert.Equal(EnergyStatuses.Attention, result.Status);
        Assert.Equal(EnergyTones.Red, result.Tone);
    }

    [Fact]
    public void LoseGoal_IntakeBelowSafeFloor_IsRed_EvenWithModestDeficit()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Lunch, 1000) }, gender: Gender.Female, weight: 50, height: 155, age: 30));

        Assert.True(result.Balance > -1000);
        Assert.Equal(EnergyStatuses.Attention, result.Status);
    }

    [Fact]
    public void LoseGoal_Surplus_IsOrange()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Dinner, 2500) }));

        Assert.Equal(EnergyStatuses.Surplus, result.Status);
        Assert.Equal(EnergyTones.Orange, result.Tone);
    }

    [Fact]
    public void GainGoal_ControlledSurplusIsGreen_DeficitIsRed_LargeSurplusOrange()
    {
        var calc = Calculator();
        var controlled = calc.Calculate(Input(direction: "gain", planned: 300, meals: new[] { Meal(MealType.Dinner, 2300) }));
        var deficit = calc.Calculate(Input(direction: "gain", planned: 300, meals: new[] { Meal(MealType.Dinner, 1600) }));
        var large = calc.Calculate(Input(direction: "gain", planned: 300, meals: new[] { Meal(MealType.Dinner, 3000) }));

        Assert.Equal(EnergyTones.Green, controlled.Tone);
        Assert.Equal(EnergyTones.Red, deficit.Tone);
        Assert.Equal(EnergyTones.Orange, large.Tone);
    }

    [Fact]
    public void MaintainGoal_NearBalanceIsGreen()
    {
        var result = Calculator().Calculate(Input(direction: "maintain", planned: 0, meals: new[] { Meal(MealType.Dinner, 2100) }));

        // 1780 + 210 = 1990 -> +110
        Assert.Equal(EnergyStatuses.Aligned, result.Status);
        Assert.Contains(result.Insights, i => i.Code == "balance_aligned");
    }

    [Fact]
    public void LowProtein_OnCompleteDay_ProducesInsight()
    {
        var result = Calculator().Calculate(Input(meals: new[] { Meal(MealType.Lunch, 1700, protein: 60) }));

        var insight = Assert.Single(result.Insights, i => i.Code == "protein_low");
        Assert.Contains("60 / 150 g", insight.Text);
    }

    [Fact]
    public void OverlappingSteps_UseDeviceOffsetOfThatDay()
    {
        // Ayni UTC aninda biten antrenman: cihaz UTC+0 iken 05:00-05:30 saatine duser, 08 saatine degil.
        var hours = Hours((5, 2000), (8, 3000));
        var steps = new EnergyStepDay(5000, 3750, 200, hours, 0, false, Day.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc));
        var workout = new EnergyWorkoutEntry(1, "Yürüyüş", WorkoutEnergyKind.Walking,
            Day.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Utc), 1800, 100);

        var overlap = EnergyBalanceCalculator.OverlappingStepsPerHour(Day, steps, new[] { workout });

        Assert.Equal(1000, overlap[5]);
        Assert.Equal(0, overlap[8]);
    }
}
