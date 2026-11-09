using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.WorkoutLab;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutLab;

/// <summary>Kalite kontrol kurallarini elle kurulan program goruntuleri uzerinde dogrular.</summary>
public class WorkoutPlanQualityCheckerTests
{
    private static SimulationWorkout W(long id, long muscle = 1, int duration = 40, decimal? fit = 70, long[]? equipment = null, bool premium = false, int? cardioScore = 10) => new()
    {
        Id = id, Title = $"W{id}", DurationMin = duration, MuscleGroupId = muscle, MuscleGroupName = $"Kas {muscle}", FitScore = fit,
        IsPremium = premium, CardioScore = cardioScore,
        RequiredEquipment = (equipment ?? Array.Empty<long>()).Select(e => new SimulationEquipment { Id = e, Name = $"E{e}" }).ToList(),
    };

    private static SimulationDay D(string code, SimulationWorkout workout, SimulationWorkout? cardio = null, int exercises = 5, long muscle = 1) => new()
    {
        DayOfWeek = code,
        DayIndex = Array.IndexOf(TrainingDayCodes.All, code),
        Workout = workout,
        Cardio = cardio,
        EstimatedDurationMin = workout.DurationMin + (cardio?.DurationMin ?? 0),
        Exercises = Enumerable.Range(1, exercises).Select(i => new SimulationExercise
        {
            ExerciseId = workout.Id * 100 + i, Name = $"E{workout.Id}-{i}", Order = i, Sets = 3, Reps = 8, RepsMax = 12, Kind = "Main",
            MuscleGroupId = muscle, MuscleGroupName = $"Kas {muscle}",
        }).ToList(),
    };

    private static WorkoutPlanSimulationResult Result(List<SimulationDay> days, int mainPool = 10, long[]? owned = null, int cardioTarget = 0, int cardioDays = 0,
        string stage = "Primary", bool includePremium = false, bool premiumLastResort = false, int preference = 45, int? target = null) => new()
        {
            AlgorithmVersion = "test", OptionsFingerprint = "x", PlanName = "p", Summary = "s",
            Goal = FitnessGoal.BuildMuscle, TrainingDays = days.Count, TargetTrainingDays = target ?? days.Count, DurationPreferenceMin = preference, DurationToleranceMin = 15,
            MaxDurationMin = preference + 15, EquipmentResolution = "Selected", PoolStage = stage, MainPoolCount = mainPool, EligibleCount = mainPool,
            Equipment = (owned ?? Array.Empty<long>()).Select(e => new SimulationEquipment { Id = e, Name = $"E{e}" }).ToList(),
            CardioTarget = cardioTarget, CardioDays = cardioDays, IncludePremium = includePremium, PremiumLastResortUsed = premiumLastResort,
            SetsRule = 3, RepsMinRule = 8, RepsMaxRule = 12, RestSecondsRule = 90, MaxExercisesPerDay = 6,
            Days = days, Metrics = WorkoutPlanSimulationProcessor.BuildMetrics(days),
        };

    private static WorkoutPlanSimulationProfile P(int? days = 3, params string[] specific) => new() { TrainingDaysPerWeek = days, TrainingDays = specific.ToList() };

    private static WorkoutPlanQualityCheck Run(WorkoutPlanSimulationResult result, string code, WorkoutPlanSimulationProfile? profile = null) =>
        WorkoutPlanQualityChecker.Evaluate(result, profile ?? P(result.Days.Count), new WorkoutPlanLabOptions()).Single(c => c.Code == code);

    [Fact]
    public void Evaluate_ReturnsTenChecks_EachWithRuleAndMessage()
    {
        var checks = WorkoutPlanQualityChecker.Evaluate(Result(new() { D("Mon", W(1)), D("Wed", W(2, 2)), D("Fri", W(3, 3)) }), P(), new WorkoutPlanLabOptions());

        Assert.Equal(10, checks.Count);
        Assert.Equal(checks.Count, checks.Select(c => c.Code).Distinct().Count());
        Assert.All(checks, c => { Assert.False(string.IsNullOrWhiteSpace(c.Rule)); Assert.False(string.IsNullOrWhiteSpace(c.Message)); });
    }

    [Fact]
    public void RepeatedWorkout_WhileUnusedCandidatesExist_Fails()
    {
        var w = W(1);
        var check = Run(Result(new() { D("Mon", w), D("Wed", w), D("Fri", W(2, 2)) }, mainPool: 5), "DuplicateExercises");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, check.Status);
    }

    [Fact]
    public void RepeatedWorkout_WhenPoolIsTooSmall_IsWarning()
    {
        var w = W(1);
        var check = Run(Result(new() { D("Mon", w), D("Wed", w), D("Fri", W(2, 2)) }, mainPool: 2), "DuplicateExercises");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, check.Status);
    }

    [Fact]
    public void SameMuscleGroupOnAdjacentDays_IsWarning_IncludingSundayToMonday()
    {
        var adjacent = Run(Result(new() { D("Mon", W(1, 1)), D("Tue", W(2, 1)) }), "ConsecutiveMuscleGroups");
        var wrap = Run(Result(new() { D("Mon", W(1, 1)), D("Wed", W(2, 2)), D("Sun", W(3, 1)) }), "ConsecutiveMuscleGroups");
        var spaced = Run(Result(new() { D("Mon", W(1, 1)), D("Wed", W(2, 1)) }), "ConsecutiveMuscleGroups");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, adjacent.Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, wrap.Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, spaced.Status);
    }

    [Fact]
    public void ExerciseLevelMuscleOverlap_OnAdjacentDays_IsWarning()
    {
        var check = Run(Result(new() { D("Mon", W(1, 1), muscle: 3), D("Tue", W(2, 2), muscle: 3) }), "ConsecutiveMuscleGroups");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, check.Status);
    }

    [Fact]
    public void WorkoutNeedingUnownedEquipment_Fails()
    {
        var check = Run(Result(new() { D("Mon", W(1, equipment: new[] { 2L })) }, owned: new[] { 1L }), "EquipmentCompatibility");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, check.Status);
        Assert.Contains("E2", check.Details.Single());
    }

    [Theory]
    [InlineData(40, WorkoutPlanQualityCheckStatus.Passed)]
    [InlineData(55, WorkoutPlanQualityCheckStatus.Warning)]
    [InlineData(61, WorkoutPlanQualityCheckStatus.Failed)]
    public void Duration_IsComparedWithPreferenceAndTolerance(int minutes, WorkoutPlanQualityCheckStatus expected)
    {
        Assert.Equal(expected, Run(Result(new() { D("Mon", W(1, duration: minutes)) }), "DurationFit").Status);
    }

    [Fact]
    public void CardioFinisher_CountsTowardsDayDuration()
    {
        var check = Run(Result(new() { D("Mon", W(1, duration: 40), cardio: W(9, 6, 25, cardioScore: 90)) }), "DurationFit");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, check.Status);
    }

    [Fact]
    public void ProgramDays_MustStayInsideAvailableDays()
    {
        var result = Result(new() { D("Mon", W(1)), D("Wed", W(2, 2)) });

        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, Run(result, "TrainingDayCount", P(null, "Mon", "Wed")).Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, Run(result, "TrainingDayCount", P(null, "Mon", "Thu")).Status);
    }

    [Fact]
    public void FewerAvailableDaysThanTarget_IsWarning()
    {
        var result = Result(new() { D("Tue", W(1)), D("Fri", W(2, 2)) }, target: 3);

        var check = Run(result, "TrainingDayCount", P(null, "Tue", "Fri"));

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, check.Status);
        Assert.Contains("hedefin (3) altında", check.Message);
    }

    [Fact]
    public void WholeWeekAvailable_DayCountMustEqualTarget()
    {
        var days = new List<SimulationDay> { D("Mon", W(1)), D("Wed", W(2, 2)), D("Fri", W(3, 3)) };

        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, Run(Result(days, target: 3), "TrainingDayCount", P(null)).Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, Run(Result(days, target: 4), "TrainingDayCount", P(null)).Status);
    }

    [Theory]
    [InlineData(75, WorkoutPlanQualityCheckStatus.Passed)]
    [InlineData(50, WorkoutPlanQualityCheckStatus.Warning)]
    [InlineData(30, WorkoutPlanQualityCheckStatus.Failed)]
    public void GoalAlignment_UsesConfiguredThresholds(int fit, WorkoutPlanQualityCheckStatus expected)
    {
        Assert.Equal(expected, Run(Result(new() { D("Mon", W(1, fit: fit)), D("Wed", W(2, 2, fit: fit)) }), "GoalAlignment").Status);
    }

    [Fact]
    public void GoalAlignment_WithoutScores_IsWarningNotFailure()
    {
        var check = Run(Result(new() { D("Mon", W(1, fit: null)) }), "GoalAlignment");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, check.Status);
        Assert.Contains("ölçülemedi", check.Message);
    }

    [Fact]
    public void Cardio_BelowTarget_IsWarning_AndAdjacentCardioDaysAreFlagged()
    {
        var cardio = W(9, 6, 15, cardioScore: 90);
        var missing = Run(Result(new() { D("Mon", W(1), cardio), D("Wed", W(2, 2)), D("Fri", W(3, 3)) }, cardioTarget: 3, cardioDays: 1), "CardioDistribution");
        var adjacent = Run(Result(new() { D("Mon", W(1), cardio), D("Tue", W(2, 2), cardio), D("Thu", W(3, 3)), D("Sat", W(4, 4)) }, cardioTarget: 2, cardioDays: 2), "CardioDistribution");
        var spread = Run(Result(new() { D("Mon", W(1), cardio), D("Wed", W(2, 2)), D("Fri", W(3, 3), cardio) }, cardioTarget: 2, cardioDays: 2), "CardioDistribution");

        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, missing.Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, adjacent.Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, spread.Status);
    }

    [Fact]
    public void ExerciseVolume_FlagsThinAndEmptyDays()
    {
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, Run(Result(new() { D("Mon", W(1), exercises: 2) }), "ExerciseVolume").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, Run(Result(new() { D("Mon", W(1), exercises: 0) }), "ExerciseVolume").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, Run(Result(new() { D("Mon", W(1), exercises: 4) }), "ExerciseVolume").Status);
    }

    [Fact]
    public void PremiumForFreeUser_FailsUnlessLastResort()
    {
        var days = new List<SimulationDay> { D("Mon", W(1, premium: true)) };

        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, Run(Result(days), "PremiumAccess").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, Run(Result(days, premiumLastResort: true), "PremiumAccess").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, Run(Result(days, includePremium: true), "PremiumAccess").Status);
    }

    [Theory]
    [InlineData("Primary", WorkoutPlanQualityCheckStatus.Passed)]
    [InlineData("AdjacentDifficulty", WorkoutPlanQualityCheckStatus.Warning)]
    [InlineData("EquipmentFreeAnyDifficulty", WorkoutPlanQualityCheckStatus.Warning)]
    public void FallbackStage_IsReported(string stage, WorkoutPlanQualityCheckStatus expected)
    {
        Assert.Equal(expected, Run(Result(new() { D("Mon", W(1)) }, stage: stage), "FallbackUsage").Status);
    }
}
