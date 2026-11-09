using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Business.Services.WorkoutLab;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutLab;

/// <summary>
/// Antrenman Laboratuvari: istenen 12 senaryo gercek WorkoutPlanGenerator ve simulasyon isleyicisiyle calistirilir.
/// Her senaryoda kullanici tablolarina hic erisilmedigi ve kalici plan/bildirim olusmadigi da dogrulanir.
/// </summary>
public class WorkoutPlanSimulationProcessorTests
{
    private static WorkoutPlanSimulationProfile Profile(
        FitnessGoal goal = FitnessGoal.BuildMuscle,
        ExperienceLevel experience = ExperienceLevel.Beginner,
        TrainingLocation location = TrainingLocation.Home,
        long[]? equipment = null,
        bool noEquipment = false,
        int? days = 3,
        string[]? specificDays = null,
        int? duration = 45,
        bool plus = false) => new()
        {
            Age = 30,
            Gender = Gender.Male,
            HeightCm = 180,
            WeightKg = 80,
            TargetWeightKg = 75,
            Goal = goal,
            ExperienceLevel = experience,
            TrainingLocation = location,
            EquipmentIds = (equipment ?? Array.Empty<long>()).ToList(),
            NoEquipment = noEquipment,
            TrainingDaysPerWeek = days,
            TrainingDays = (specificDays ?? Array.Empty<string>()).ToList(),
            WorkoutDurationMin = duration,
            IsPlus = plus,
        };

    private static WorkoutPlanQualityCheck CheckOf(List<WorkoutPlanQualityCheck> checks, string code) => checks.Single(c => c.Code == code);

    private static void AssertNoUserDataTouched(WorkoutLabTestCatalog catalog)
    {
        var mock = catalog.Uow.Mock;
        mock.Verify(u => u.Repository<UserProfile>(), Times.Never);
        mock.Verify(u => u.Repository<UserGoal>(), Times.Never);
        mock.Verify(u => u.Repository<UserEquipment>(), Times.Never);
        mock.Verify(u => u.Repository<Subscription>(), Times.Never);
        mock.Verify(u => u.Repository<WorkoutPlan>(), Times.Never);
        mock.Verify(u => u.Repository<WorkoutPlanDay>(), Times.Never);
        mock.Verify(u => u.Repository<WorkoutPlanExercise>(), Times.Never);
        mock.Verify(u => u.Repository<PersonalizationJob>(), Times.Never);
        mock.Verify(u => u.Repository<NotificationDispatch>(), Times.Never);
        Assert.Empty(catalog.Uow.Set<WorkoutPlan>());
        Assert.Empty(catalog.Uow.Set<PersonalizationJob>());
        Assert.Empty(catalog.Uow.Set<NotificationDispatch>());
    }

    // 1. Beginner + ev + ekipmansiz + kas gelistirme
    [Fact]
    public async Task Scenario01_BeginnerHomeNoEquipmentBuildMuscle_UsesOnlyBodyweightWorkouts()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (sim, result, checks) = await catalog.RunAsync(Profile(noEquipment: true));

        Assert.Equal(WorkoutPlanSimulationStatus.Completed, sim.Status);
        Assert.NotNull(result);
        Assert.Equal("None", result!.EquipmentResolution);
        Assert.Empty(result.Equipment);
        Assert.All(result.Days, d => Assert.Empty(d.Workout.RequiredEquipment));
        Assert.All(result.Days, d => Assert.Equal(DifficultyLevel.Beginner, d.Workout.Difficulty));
        Assert.Equal(new[] { "Mon", "Wed", "Fri" }, result.Days.Select(d => d.DayOfWeek));
        Assert.All(result.Days.SelectMany(d => d.Exercises), e => Assert.Equal((3, 8, 12, 90), (e.Sets, e.Reps!.Value, e.RepsMax!.Value, e.RestSeconds!.Value)));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "EquipmentCompatibility").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "FallbackUsage").Status);
        Assert.Equal(WorkoutPlanSimulationStatus.Completed, sim.Status);
        Assert.Equal(sim.PassedCheckCount + sim.WarningCheckCount + sim.FailedCheckCount, checks.Count);
        AssertNoUserDataTouched(catalog);
    }

    // 2. Intermediate + spor salonu + tum ekipmanlar + kuvvet
    [Fact]
    public async Task Scenario02_IntermediateGymStrength_UsesWholeEquipmentCatalogAndStrengthRules()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(FitnessGoal.GetStronger, ExperienceLevel.Intermediate, TrainingLocation.Gym, duration: 60));

        Assert.Equal("GymAllEquipment", result!.EquipmentResolution);
        Assert.Equal(5, result.Equipment.Count);
        // Kuvvet hedefinde en yuksek Strength puanli barbell antrenmanlari secilir.
        // Orta seviye hedefi 4 gun; uc barbell antrenmaninin hepsi programda.
        Assert.Equal(4, result.Days.Count);
        Assert.Subset(result.Days.Select(d => d.Workout.Id).ToHashSet(), new HashSet<long> { 301, 302, 303 });
        Assert.All(result.Days.SelectMany(d => d.Exercises), e => Assert.Equal((4, 3, 6, 120), (e.Sets, e.Reps!.Value, e.RepsMax!.Value, e.RestSeconds!.Value)));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "GoalAlignment").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "EquipmentCompatibility").Status);
        Assert.True(result.Metrics.UsedEquipmentCount >= 2);
        AssertNoUserDataTouched(catalog);
    }

    // 3. Advanced + spor salonu + yag kaybi
    [Fact]
    public async Task Scenario03_AdvancedGymLoseWeight_KeepsResistanceDaysAndAddsCardioFinishers()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(FitnessGoal.LoseWeight, ExperienceLevel.Advanced, TrainingLocation.Gym, days: 4, duration: 90));

        Assert.All(result!.Days, d => Assert.False(d.Workout.IsCardioDominant));
        Assert.All(result.Days, d => Assert.Equal(DifficultyLevel.Advanced, d.Workout.Difficulty));
        Assert.Equal(3, result.CardioTarget);
        Assert.Equal(3, result.CardioDays);
        Assert.Equal(3, result.Days.Count(d => d.Cardio is not null));
        Assert.All(result.Days.Where(d => d.Cardio is not null), d => Assert.True(d.EstimatedDurationMin <= result.MaxDurationMin));
        Assert.All(result.Days.Where(d => d.Cardio is not null), d => Assert.NotEmpty(d.CardioExercises));
        Assert.All(result.Days.SelectMany(d => d.Exercises), e => Assert.Equal((4, 10, 15, 60), (e.Sets, e.Reps!.Value, e.RepsMax!.Value, e.RestSeconds!.Value)));
        Assert.NotEqual(WorkoutPlanQualityCheckStatus.Failed, CheckOf(checks, "CardioDistribution").Status);
        AssertNoUserDataTouched(catalog);
    }

    // 4. Yalnizca 2 musait gun: hedef (3) karsilanamaz, program bu iki gunde olur (uyari).
    [Fact]
    public async Task Scenario04_OnlyTwoAvailableDays_UsesThoseDaysWithWarning()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: null, specificDays: new[] { "Tue", "Fri" }));

        Assert.Equal(new[] { "Tue", "Fri" }, result!.Days.Select(d => d.DayOfWeek));
        Assert.Equal(3, result.TargetTrainingDays);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "TrainingDayCount").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "ConsecutiveMuscleGroups").Status);
    }

    // 5. Ileri + yag kaybi: algoritma 6 gun secer; ekipmansiz havuz kucukse tekrar kacinilmaz (uyari).
    [Fact]
    public async Task Scenario05_AdvancedLoseWeight_GetsSixDays()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(FitnessGoal.LoseWeight, ExperienceLevel.Advanced, noEquipment: true, days: null));

        Assert.Equal(6, result!.TargetTrainingDays);
        Assert.Equal(new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }, result.Days.Select(d => d.DayOfWeek));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "TrainingDayCount").Status);
    }

    // 5b. Beginner, tum hafta musait: 3 gun, aralarinda dinlenme gunu olacak sekilde.
    [Fact]
    public async Task Scenario05b_BeginnerWholeWeek_GetsThreeSpacedDays()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: null));

        Assert.Equal(new[] { "Mon", "Wed", "Fri" }, result!.Days.Select(d => d.DayOfWeek));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "TrainingDayCount").Status);
    }

    // 6. 20 dakika: tolerans dahil en fazla 35 dk
    [Fact]
    public async Task Scenario06_TwentyMinutes_OnlyWorkoutsWithinToleranceAreUsed()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, duration: 20));

        Assert.Equal(35, result!.MaxDurationMin);
        Assert.All(result.Days, d => Assert.True(d.EstimatedDurationMin <= 35));
        var duration = CheckOf(checks, "DurationFit");
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, duration.Status); // 25-30 dk antrenmanlar tercihten uzun ama tolerans icinde
        Assert.NotEqual(WorkoutPlanQualityCheckStatus.Failed, duration.Status);
    }

    // 7. 90 dakika
    [Fact]
    public async Task Scenario07_NinetyMinutes_LongWorkoutsAreEligibleAndDurationPasses()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(FitnessGoal.BuildMuscle, ExperienceLevel.Advanced, TrainingLocation.Gym, duration: 90));

        Assert.Equal(105, result!.MaxDurationMin);
        Assert.Contains(result.Days, d => d.Workout.DurationMin >= 70);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "DurationFit").Status);
    }

    // 8. Plus kullanicisi: Premium antrenman aday havuzunda
    [Fact]
    public async Task Scenario08_PlusUser_CanReceivePremiumWorkout()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();
        catalog.Workout(150, "Premium BW Hipertrofi", DifficultyLevel.Beginner, 30, muscle: 4, scores: (100, 60, 10), premium: true);

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, plus: true));

        Assert.True(result!.IncludePremium);
        Assert.Contains(result.Days, d => d.Workout.Id == 150 && d.Workout.IsPremium);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "PremiumAccess").Status);
    }

    // 9. Free kullanicisi: ayni katalogda Premium atanmaz
    [Fact]
    public async Task Scenario09_FreeUser_NeverReceivesPremiumWhenFreeWorkoutsExist()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();
        catalog.Workout(150, "Premium BW Hipertrofi", DifficultyLevel.Beginner, 30, muscle: 4, scores: (100, 60, 10), premium: true);

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, plus: false));

        Assert.False(result!.IncludePremium);
        Assert.DoesNotContain(result.Days, d => d.Workout.IsPremium || d.Cardio?.IsPremium == true);
        Assert.False(result.PremiumLastResortUsed);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "PremiumAccess").Status);
    }

    // 10a. Uygun ekipmani olmayan kullanici: ekipmansiz antrenman da yoksa program uretilemez (gercek hata mesaji).
    [Fact]
    public async Task Scenario10a_NoMatchingEquipmentAndNoBodyweightWorkout_FailsWithEquipmentMessage()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(301, "Squat Günü", DifficultyLevel.Beginner, 40, muscle: 3, equipment: new[] { WorkoutLabTestCatalog.Barbell });

        var (sim, result, checks) = await catalog.RunAsync(Profile(equipment: new[] { WorkoutLabTestCatalog.Dumbbell }));

        Assert.Equal(WorkoutPlanSimulationStatus.Failed, sim.Status);
        Assert.Null(result);
        Assert.Empty(checks);
        Assert.Contains("ekipman", sim.ErrorMessage);
        Assert.NotNull(sim.AlgorithmVersion);
        AssertNoUserDataTouched(catalog);
    }

    // 10b. Uygun ekipman yok ama baska seviyede bodyweight antrenman var: Fallback-2 devreye girer.
    [Fact]
    public async Task Scenario10b_NoMatchingEquipment_FallsBackToBodyweightOfAnyDifficulty()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(301, "Squat Günü", DifficultyLevel.Beginner, 40, muscle: 3, equipment: new[] { WorkoutLabTestCatalog.Barbell });
        catalog.Workout(501, "İleri BW", DifficultyLevel.Advanced, 80, muscle: 5, scores: (70, 50, 20));

        var (_, result, checks) = await catalog.RunAsync(Profile(equipment: new[] { WorkoutLabTestCatalog.Dumbbell }, duration: 30));

        Assert.Equal(nameof(WorkoutCandidatePoolStage.EquipmentFreeAnyDifficulty), result!.PoolStage);
        Assert.All(result.Days, d => Assert.Equal(501, d.Workout.Id));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "FallbackUsage").Status);
        // Fallback-2 sure filtresi uygulamaz: 80 dk antrenman 45 dk sinirini asar.
        Assert.Equal(WorkoutPlanQualityCheckStatus.Failed, CheckOf(checks, "DurationFit").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "EquipmentCompatibility").Status);
    }

    // 11a. Aktif katalog bos
    [Fact]
    public async Task Scenario11a_EmptyActiveCatalog_FailsWithCatalogMessage()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(101, "Pasif", DifficultyLevel.Beginner, 30, muscle: 1, active: false);

        var (sim, _, _) = await catalog.RunAsync(Profile(noEquipment: true));

        Assert.Equal(WorkoutPlanSimulationStatus.Failed, sim.Status);
        Assert.Contains("Aktif antrenman kataloğunda hiç kayıt", sim.ErrorMessage);
    }

    // 11b. Katalog yetersiz: tek uygun antrenman, az egzersiz -> tekrar ve hacim uyarilari.
    [Fact]
    public async Task Scenario11b_InsufficientCatalog_ProducesPlanWithRepeatAndVolumeWarnings()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(101, "Tek Antrenman", DifficultyLevel.Beginner, 30, muscle: 1, scores: (70, 40, 10), exerciseCount: 2);

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: 3));

        Assert.All(result!.Days, d => Assert.Equal(101, d.Workout.Id));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "DuplicateExercises").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "ExerciseVolume").Status);
    }

    // 12. Belirli gunler secildi
    [Fact]
    public async Task Scenario12_SpecificDays_ProgramUsesExactlyThoseDays()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (sim, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: null, specificDays: new[] { "Sat", "Mon", "Thu" }));

        Assert.Equal(new[] { "Mon", "Thu", "Sat" }, result!.Days.Select(d => d.DayOfWeek));
        Assert.True(result.UsedPreferredDays);
        Assert.Equal(3, sim.TrainingDaysPerWeek);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "TrainingDayCount").Status);
    }

    // Fallback-1: seviyeye uygun aday yok -> bitisik zorluk.
    [Fact]
    public async Task AdjacentDifficultyFallback_IsReported()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(201, "Orta BW", DifficultyLevel.Intermediate, 30, muscle: 1, scores: (70, 40, 10));
        catalog.Workout(202, "Orta BW 2", DifficultyLevel.Intermediate, 30, muscle: 2, scores: (70, 40, 10));

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: 2));

        Assert.Equal(nameof(WorkoutCandidatePoolStage.AdjacentDifficulty), result!.PoolStage);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "FallbackUsage").Status);
    }

    // Fallback-3: Free kullanicinin ucretsiz havuzu bos -> Premium son care.
    [Fact]
    public async Task PremiumLastResort_ForFreeUser_IsWarnedNotFailed()
    {
        var catalog = new WorkoutLabTestCatalog();
        catalog.Workout(150, "Yalnızca Premium", DifficultyLevel.Beginner, 30, muscle: 1, scores: (70, 40, 10), premium: true);

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: 2));

        Assert.True(result!.PremiumLastResortUsed);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "PremiumAccess").Status);
        Assert.Equal(WorkoutPlanQualityCheckStatus.Warning, CheckOf(checks, "FallbackUsage").Status);
    }

    [Fact]
    public async Task UnknownEquipmentIds_AreIgnoredAndReported()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, _) = await catalog.RunAsync(Profile(equipment: new[] { WorkoutLabTestCatalog.Dumbbell, 999L }));

        Assert.Equal(new long[] { WorkoutLabTestCatalog.Dumbbell }, result!.Equipment.Select(e => e.Id));
        Assert.Equal(new long[] { 999 }, result.UnknownEquipmentIds);
    }

    [Fact]
    public async Task EquipmentFlag_TrueUsesWholeCatalog_FalseUsesNone_RegardlessOfLocation()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var equipped = Profile(location: TrainingLocation.Home);
        equipped.HasEquipment = true;
        var (_, withEquipment, _) = await catalog.RunAsync(equipped);

        var bodyweight = Profile(location: TrainingLocation.Gym);
        bodyweight.HasEquipment = false;
        var (_, withoutEquipment, checks) = await catalog.RunAsync(bodyweight);

        Assert.Equal("AllEquipment", withEquipment!.EquipmentResolution);
        Assert.Equal(5, withEquipment.Equipment.Count);
        Assert.Equal("None", withoutEquipment!.EquipmentResolution);
        Assert.Empty(withoutEquipment.Equipment);
        Assert.All(withoutEquipment.Days, d => Assert.Empty(d.Workout.RequiredEquipment));
        Assert.Equal(WorkoutPlanQualityCheckStatus.Passed, CheckOf(checks, "EquipmentCompatibility").Status);
    }

    [Fact]
    public async Task DefaultsAreUsed_WhenOptionalFieldsAreEmpty()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, result, checks) = await catalog.RunAsync(Profile(noEquipment: true, days: null, duration: null));

        Assert.Equal(45, result!.DurationPreferenceMin);
        Assert.True(result.UsedDefaultDuration);
        Assert.Equal(3, result.TrainingDays);
        Assert.Contains(CheckOf(checks, "TrainingDayCount").Details, d => d.Contains("tüm hafta"));
    }

    [Fact]
    public async Task Result_StoresAlgorithmVersionAndOptionsFingerprint()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (sim, result, _) = await catalog.RunAsync(Profile(noEquipment: true));
        var changed = new WorkoutPlanGenerationOptions { RepeatPenaltyScore = 51 };

        Assert.Equal(MerfitCustomerApi.Business.Services.Personalization.Generators.WorkoutPlanGenerator.AlgorithmVersion, sim.AlgorithmVersion);
        Assert.Equal(result!.OptionsFingerprint, sim.OptionsFingerprint);
        Assert.Equal(12, sim.OptionsFingerprint!.Length);
        Assert.NotEqual(WorkoutPlanSimulationProcessor.ComputeOptionsFingerprint(new WorkoutPlanGenerationOptions()),
            WorkoutPlanSimulationProcessor.ComputeOptionsFingerprint(changed));
        Assert.Equal(WorkoutPlanSimulationProcessor.ComputeOptionsFingerprint(new WorkoutPlanGenerationOptions()),
            WorkoutPlanSimulationProcessor.ComputeOptionsFingerprint(new WorkoutPlanGenerationOptions()));
    }

    [Fact]
    public async Task SameProfile_ProducesSameProgram_Deterministically()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        var (_, first, _) = await catalog.RunAsync(Profile(FitnessGoal.GetStronger, ExperienceLevel.Intermediate, TrainingLocation.Gym, days: 5));
        var (_, second, _) = await catalog.RunAsync(Profile(FitnessGoal.GetStronger, ExperienceLevel.Intermediate, TrainingLocation.Gym, days: 5));

        Assert.Equal(
            first!.Days.Select(d => (d.DayOfWeek, d.Workout.Id, d.Cardio?.Id)),
            second!.Days.Select(d => (d.DayOfWeek, d.Workout.Id, d.Cardio?.Id)));
    }

    [Fact]
    public async Task NoPendingSimulation_ReturnsFalse()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        Assert.False(await catalog.CreateProcessor().ProcessNextAsync());
    }

    [Fact]
    public async Task CompletedAndEvaluatedSimulations_AreNotReprocessed()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();
        var sim = catalog.Enqueue(Profile(noEquipment: true));
        sim.Status = WorkoutPlanSimulationStatus.Completed;

        Assert.False(await catalog.CreateProcessor().ProcessNextAsync());
    }

    [Fact]
    public async Task StaleProcessingSimulation_IsRetried_ThenFailedWhenAttemptsAreExhausted()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();
        var sim = catalog.Enqueue(Profile(noEquipment: true));
        sim.Status = WorkoutPlanSimulationStatus.Processing;
        sim.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        sim.AttemptCount = 1;

        Assert.True(await catalog.CreateProcessor().ProcessNextAsync());
        Assert.Equal(WorkoutPlanSimulationStatus.Completed, sim.Status);

        var stuck = catalog.Enqueue(Profile(noEquipment: true));
        stuck.Status = WorkoutPlanSimulationStatus.Processing;
        stuck.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        stuck.AttemptCount = 2;

        Assert.False(await catalog.CreateProcessor().ProcessNextAsync());
        Assert.Equal(WorkoutPlanSimulationStatus.Failed, stuck.Status);
        Assert.Contains("zaman aşımı", stuck.ErrorMessage);
    }
}
