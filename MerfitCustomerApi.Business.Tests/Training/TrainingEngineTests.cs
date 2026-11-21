using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Training;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Training;

/// <summary>
/// Kisisellestirilmis antrenman motorunun saf (veritabanisiz) kurallari: split secimi, egzersiz secimi,
/// recete, haftalik hacim, cift progresyon, gelisim analizi, toparlanma / deload ve AI yanit dogrulamasi.
/// </summary>
public class TrainingEngineTests
{
    private static readonly TrainingEngineOptions Options = new();
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<long> AllEquipment = new() { TrainingTestCatalog.Barbell, TrainingTestCatalog.Dumbbell, TrainingTestCatalog.Machine, TrainingTestCatalog.Cable };

    private static readonly DayOfWeek[] Week =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private static TrainingProfile Profile(
        ExperienceLevel experience = ExperienceLevel.Beginner,
        int days = 3,
        TrainingFocus focus = TrainingFocus.Hypertrophy,
        int minutes = 60,
        IEnumerable<long>? equipment = null,
        bool gym = true) => new()
    {
        UserId = 1,
        Experience = experience,
        RequestedDaysPerWeek = days,
        AvailableDays = Week,
        Focus = focus,
        SessionMinutes = minutes,
        EquipmentIds = (equipment ?? AllEquipment).ToHashSet(),
        HasGymAccess = gym,
    };

    private static async Task<(GeneratedProgram Program, IReadOnlyDictionary<long, CatalogExercise> Catalog)> BuildAsync(
        TrainingProfile profile, TrainingHistory? history = null, ProgramAdjustments? adjustments = null)
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var program = new ProgramBuilder(Options).Build(profile, catalog.Values.ToList(), history ?? TrainingHistory.Empty, adjustments ?? ProgramAdjustments.None, Now);
        return (program, catalog);
    }

    private static void AssertStructurallyValid(GeneratedProgram program, IReadOnlyDictionary<long, CatalogExercise> catalog, TrainingProfile profile)
    {
        var errors = ProgramValidator.Validate(program, catalog, profile, Options);
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
        foreach (var day in program.Days)
        {
            Assert.NotEmpty(day.Exercises);
            Assert.Equal(day.Exercises.Count, day.Exercises.Select(e => e.Exercise.Id).Distinct().Count());
            Assert.All(day.Exercises, e => Assert.True(catalog.ContainsKey(e.Exercise.Id)));

            // Bilesik hareketler izolasyonlardan once (core en sonda olabilir).
            var nonCore = day.Exercises.Where(e => e.TargetRegion != MuscleRegion.Core).ToList();
            var lastCompound = nonCore.FindLastIndex(e => e.Exercise.Mechanic == ExerciseMechanicType.Compound);
            var firstIsolation = nonCore.FindIndex(e => e.Exercise.Mechanic == ExerciseMechanicType.Isolation);
            if (lastCompound >= 0 && firstIsolation >= 0)
            {
                Assert.True(lastCompound < firstIsolation, $"{day.Template.Title}: bilesik hareket izolasyondan sonra geldi.");
            }

            // Ayni gunde ayni bolge icin en fazla 2 egzersiz (gereksiz tekrar yok).
            Assert.All(day.Exercises.GroupBy(e => e.TargetRegion), g => Assert.True(g.Count() <= Options.MaxExercisesPerRegionPerDay));
        }
    }

    /// <summary>
    /// Tum deneyim x gun x hedef x sure x ekipman kombinasyonlarinda uretilen program dogrulayicidan gecer
    /// (katalog disi egzersiz yok, ekipman uygun, set/tekrar/RIR/dinlenme sinirlarda, hacim ust sinirin altinda).
    /// </summary>
    [Fact]
    public async Task AllProfileCombinations_ProduceValidPrograms()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var builder = new ProgramBuilder(Options);
        var equipmentSets = new[]
        {
            (AllEquipment.AsEnumerable(), true),
            (new[] { TrainingTestCatalog.Dumbbell }.AsEnumerable(), false),
            (Array.Empty<long>().AsEnumerable(), false),
        };
        var count = 0;
        foreach (var experience in Enum.GetValues<ExperienceLevel>())
        foreach (var days in new[] { 2, 3, 4, 5, 6 })
        foreach (var focus in Enum.GetValues<TrainingFocus>())
        foreach (var minutes in new[] { 30, 45, 60, 90 })
        foreach (var (equipment, gym) in equipmentSets)
        {
            var profile = Profile(experience, days, focus, minutes, equipment, gym);
            var program = builder.Build(profile, catalog.Values.ToList(), TrainingHistory.Empty, ProgramAdjustments.None, Now);
            var errors = ProgramValidator.Validate(program, catalog, profile, Options);
            Assert.True(errors.Count == 0, $"{experience}/{days}g/{focus}/{minutes}dk/gym={gym}: {string.Join(" | ", errors)}");
            Assert.All(program.Days, d => Assert.True(d.EstimatedMinutes <= minutes + 10, $"{experience}/{days}g/{focus}/{minutes}dk: {d.EstimatedMinutes} dk"));
            count++;
        }

        Assert.True(count > 500);
    }

    // ---- Senaryo 1: Ilk kez kullanan kullanici (gecmis yok) ----

    [Fact]
    public async Task FirstTimeUser_GetsFullBody_WithCalibration_AndNoInventedWeights()
    {
        var profile = Profile(ExperienceLevel.Beginner, days: 3);
        var (program, catalog) = await BuildAsync(profile);

        Assert.Equal("FullBody", program.Split.SplitType);
        Assert.Equal(3, program.Days.Count);
        AssertStructurallyValid(program, catalog, profile);

        var all = program.Days.SelectMany(d => d.Exercises).ToList();
        Assert.All(all, e => Assert.Null(e.SuggestedWeightKg));
        Assert.All(all, e => Assert.Contains(e.WeightGuidance, new[] { WeightGuidanceType.RampUp, WeightGuidanceType.Bodyweight }));
        // Baslangic: hicbir sette tukenis yok (hedef RIR >= 2).
        Assert.All(all, e => Assert.True(e.Prescription.TargetRir >= 2));
    }

    // ---- Senaryo 2: 2 gun ----

    [Fact]
    public async Task TwoDaysPerWeek_FullBody_EachDayTrainsUpperAndLower()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 2);
        var (program, catalog) = await BuildAsync(profile);

        Assert.Equal("FullBody", program.Split.SplitType);
        Assert.Equal(2, program.Days.Count);
        AssertStructurallyValid(program, catalog, profile);
        Assert.All(program.Days, d =>
        {
            Assert.Contains(d.Exercises, e => MuscleRegionMapper.IsLowerBody(e.TargetRegion));
            Assert.Contains(d.Exercises, e => e.TargetRegion is MuscleRegion.Chest or MuscleRegion.Back or MuscleRegion.Shoulders);
        });
    }

    // ---- Senaryo 3: Orta seviye, 4 gun ----

    [Fact]
    public async Task Intermediate_FourDays_UpperLower_EachMuscleTwicePerWeek()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4);
        var (program, catalog) = await BuildAsync(profile);

        Assert.Equal("UpperLower", program.Split.SplitType);
        Assert.Equal(4, program.Days.Count);
        AssertStructurallyValid(program, catalog, profile);
        Assert.Equal(2, program.Days.Count(d => d.Exercises.Any(e => e.TargetRegion == MuscleRegion.Chest)));
        Assert.Equal(2, program.Days.Count(d => d.Exercises.Any(e => e.TargetRegion == MuscleRegion.Quadriceps)));
    }

    // ---- Senaryo 4: Ileri seviye, 5 gun, hipertrofi ----

    [Fact]
    public async Task Advanced_FiveDays_Hypertrophy_UsesCustomSplit_AndRespectsVolumeCaps()
    {
        var profile = Profile(ExperienceLevel.Advanced, days: 5, focus: TrainingFocus.Hypertrophy, minutes: 75);
        var (program, catalog) = await BuildAsync(profile);

        Assert.Equal("UpperLowerPushPullLegs", program.Split.SplitType);
        Assert.Equal(5, program.Days.Count);
        AssertStructurallyValid(program, catalog, profile);
        foreach (var v in program.WeeklyVolume)
        {
            var cap = MuscleRegionMapper.IsLarge(v.Region) ? Options.MaxWeeklySetsLargeMuscle : Options.MaxWeeklySetsSmallMuscle;
            Assert.True(v.PlannedTotalSets <= cap + 0.5m, $"{v.Region}: {v.PlannedTotalSets} > {cap}");
        }

        // Ileri seviye bilesiklerde hipertrofi icin RIR 1; izolasyonlar 10-15.
        var compounds = program.Days.SelectMany(d => d.Exercises).Where(e => e.Exercise.Mechanic == ExerciseMechanicType.Compound && !e.Exercise.IsBodyweight);
        Assert.All(compounds, e => Assert.Equal(1, e.Prescription.TargetRir));
    }

    // ---- Senaryo 5: Sinirli ekipman (yalnizca dambil + vucut agirligi) ----

    [Fact]
    public async Task LimitedEquipment_DumbbellOnly_SelectsOnlyAvailableExercises()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 3, equipment: new[] { TrainingTestCatalog.Dumbbell }, gym: false);
        var (program, catalog) = await BuildAsync(profile);

        AssertStructurallyValid(program, catalog, profile);
        var all = program.Days.SelectMany(d => d.Exercises).ToList();
        Assert.All(all, e => Assert.True(
            e.Exercise.RequiredEquipmentIds.All(id => id == TrainingTestCatalog.Dumbbell),
            $"{e.Exercise.Name} ekipmani yok."));
        Assert.DoesNotContain(all, e => e.Exercise.Name.StartsWith("Barbell", StringComparison.Ordinal));
    }

    // ---- Senaryo 6: Hedef - kuvvet ----

    [Fact]
    public async Task StrengthGoal_MainLiftsUseLowReps_LongRest()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4, focus: TrainingFocus.Strength);
        var (program, catalog) = await BuildAsync(profile);

        AssertStructurallyValid(program, catalog, profile);
        foreach (var day in program.Days)
        {
            var first = day.Exercises[0];
            Assert.Equal(ExerciseMechanicType.Compound, first.Exercise.Mechanic);
            Assert.True(first.Prescription.RepsMax <= 8, $"{first.Exercise.Name}: {first.Prescription.RepsMin}-{first.Prescription.RepsMax}");
            Assert.True(first.Prescription.RestSeconds >= 150);
        }
    }

    // ---- Senaryo 7: Hedef - yag kaybi ----

    [Fact]
    public async Task FatLossGoal_ModerateReps_ShorterRest()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 3, focus: TrainingFocus.FatLoss);
        var (program, catalog) = await BuildAsync(profile);

        AssertStructurallyValid(program, catalog, profile);
        var all = program.Days.SelectMany(d => d.Exercises).Where(e => e.Prescription.DurationSeconds is null).ToList();
        Assert.All(all, e => Assert.InRange(e.Prescription.RepsMin, 8, 15));
        Assert.All(all, e => Assert.True(e.Prescription.RestSeconds <= 90));
    }

    [Fact]
    public async Task HypertrophyGoal_IsolationsUseHigherReps_ThanCompounds()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4, focus: TrainingFocus.Hypertrophy);
        var (program, _) = await BuildAsync(profile);

        var all = program.Days.SelectMany(d => d.Exercises).Where(e => e.Prescription.DurationSeconds is null && !e.Exercise.IsBodyweight).ToList();
        Assert.All(all.Where(e => e.Exercise.Mechanic == ExerciseMechanicType.Compound), e => Assert.Equal((6, 10), (e.Prescription.RepsMin, e.Prescription.RepsMax)));
        Assert.All(all.Where(e => e.Exercise.Mechanic == ExerciseMechanicType.Isolation), e => Assert.Equal((10, 15), (e.Prescription.RepsMin, e.Prescription.RepsMax)));
    }

    [Fact]
    public async Task BeginnerAskingForSixDays_IsCappedWithExplanation()
    {
        var profile = Profile(ExperienceLevel.Beginner, days: 6);
        var (program, _) = await BuildAsync(profile);

        Assert.Equal(Options.BeginnerMaxDaysPerWeek, program.Days.Count);
        Assert.Contains(program.Split.Reasons, r => r.Contains("Başlangıç", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AvoidedExercise_AndLimitedRegion_AreNeverSelected_PreferredIsUsed()
    {
        var bench = TrainingTestCatalog.IdOf("Barbell Bench Press");
        var dbPress = TrainingTestCatalog.IdOf("Dumbbell Incline Press");
        var profile = Profile(ExperienceLevel.Intermediate, days: 4) with
        {
            AvoidedExerciseIds = new HashSet<long> { bench },
            PreferredExerciseIds = new HashSet<long> { dbPress },
            LimitedRegions = new HashSet<MuscleRegion> { MuscleRegion.Calves },
        };
        var (program, catalog) = await BuildAsync(profile);

        AssertStructurallyValid(program, catalog, profile);
        var ids = program.Days.SelectMany(d => d.Exercises).Select(e => e.Exercise.Id).ToList();
        Assert.DoesNotContain(bench, ids);
        Assert.Contains(dbPress, ids);
        Assert.DoesNotContain(program.Days.SelectMany(d => d.Exercises), e => e.TargetRegion == MuscleRegion.Calves);
    }

    [Fact]
    public async Task SameInput_ProducesSameProgram()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4);
        var (a, _) = await BuildAsync(profile);
        var (b, _) = await BuildAsync(profile);

        Assert.Equal(
            a.Days.SelectMany(d => d.Exercises.Select(e => (d.Order, e.Exercise.Id, e.Prescription.Sets))),
            b.Days.SelectMany(d => d.Exercises.Select(e => (d.Order, e.Exercise.Id, e.Prescription.Sets))));
    }

    [Fact]
    public async Task SecondaryMuscles_CountAsHalfSets_InWeeklyVolume()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4);
        var (program, _) = await BuildAsync(profile);

        var triceps = program.WeeklyVolume.Single(v => v.Region == MuscleRegion.Triceps);
        var expectedIndirect = program.Days.SelectMany(d => d.Exercises)
            .Where(e => e.TargetRegion != MuscleRegion.Triceps && e.Exercise.SecondaryRegions.Contains(MuscleRegion.Triceps))
            .Sum(e => e.Prescription.Sets * Options.SecondaryMuscleSetWeight);
        Assert.Equal(expectedIndirect, triceps.PlannedIndirectSets);
        Assert.True(triceps.PlannedIndirectSets > 0);
    }

    [Fact]
    public async Task TimeBudget_ShortSessions_HaveFewerSetsThanLongSessions()
    {
        var (shortProgram, catalog) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 3, minutes: 30));
        var (longProgram, _) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 3, minutes: 75));

        AssertStructurallyValid(shortProgram, catalog, Profile(ExperienceLevel.Intermediate, days: 3, minutes: 30));
        Assert.True(shortProgram.Days.Sum(d => d.Exercises.Sum(e => e.Prescription.Sets)) < longProgram.Days.Sum(d => d.Exercises.Sum(e => e.Prescription.Sets)));
        Assert.All(shortProgram.Days, d => Assert.True(d.EstimatedMinutes <= 35, $"{d.EstimatedMinutes} dk"));
    }

    [Fact]
    public async Task Alternatives_ComeFromCatalog_AndTargetSameRegion()
    {
        var (program, catalog) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 4));
        foreach (var e in program.Days.SelectMany(d => d.Exercises))
        {
            Assert.DoesNotContain(e.Exercise.Id, e.AlternativeExerciseIds);
            Assert.All(e.AlternativeExerciseIds, id => Assert.Equal(e.Exercise.PrimaryRegion, catalog[id].PrimaryRegion));
        }
    }

    [Fact]
    public async Task MissingMetadata_IsInferred_AndReported()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var curl = catalog[TrainingTestCatalog.IdOf("Dumbbell Curl")];
        var goblet = catalog[TrainingTestCatalog.IdOf("Goblet Squat")];

        Assert.True(curl.MechanicInferred);
        Assert.Equal(ExerciseMechanicType.Isolation, curl.Mechanic);
        Assert.Equal(ExerciseMechanicType.Compound, goblet.Mechanic);
        Assert.Equal(2m, curl.LoadIncrementKg);

        var (program, _) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 3));
        Assert.NotEmpty(program.MetadataWarnings);
    }

    // ---- Kotu devamlilik ----

    [Fact]
    public async Task PoorAdherence_ReducesWeeklyVolume_WithExplanation()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4);
        var (normal, _) = await BuildAsync(profile);
        var (reduced, _) = await BuildAsync(profile, new TrainingHistory { ScheduledSessionsLast4Weeks = 16, CompletedPlanSessionsLast4Weeks = 6 });

        Assert.True(reduced.WeeklyVolume.Sum(v => v.TargetSets) < normal.WeeklyVolume.Sum(v => v.TargetSets));
        Assert.Contains(reduced.Notes, n => n.Contains("tamamlandı", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deload_ReducesSets_AndKeepsDistanceFromFailure()
    {
        var profile = Profile(ExperienceLevel.Intermediate, days: 4);
        var (normal, catalog) = await BuildAsync(profile);
        var (deload, _) = await BuildAsync(profile, adjustments: new ProgramAdjustments { IsDeload = true });

        Assert.True(deload.IsDeload);
        AssertStructurallyValid(deload, catalog, profile);
        Assert.True(deload.Days.Sum(d => d.Exercises.Sum(e => e.Prescription.Sets)) < normal.Days.Sum(d => d.Exercises.Sum(e => e.Prescription.Sets)));
        Assert.All(deload.Days.SelectMany(d => d.Exercises), e => Assert.True(e.Prescription.TargetRir >= 3));
    }

    [Fact]
    public async Task History_GivesStartingWeight_OnlyFromRealSets()
    {
        var bench = TrainingTestCatalog.IdOf("Barbell Bench Press");
        var history = new TrainingHistory
        {
            ExposuresByExercise = new Dictionary<long, IReadOnlyList<ExerciseExposure>>
            {
                [bench] = new[] { Exposure(bench, 1, Now.AddDays(-5), 60m, 8, 8, 8) },
            },
            CurrentProgramExerciseIds = new HashSet<long> { bench },
        };
        var (program, _) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 4), history);

        var planned = program.Days.SelectMany(d => d.Exercises).First(e => e.Exercise.Id == bench);
        Assert.Equal(WeightGuidanceType.FromHistory, planned.WeightGuidance);
        Assert.Equal(60m, planned.SuggestedWeightKg);
        Assert.All(program.Days.SelectMany(d => d.Exercises).Where(e => e.Exercise.Id != bench && !e.Exercise.IsBodyweight),
            e => Assert.Null(e.SuggestedWeightKg));
    }

    // ---- Progresyon ----

    private static ExerciseExposure Exposure(long exerciseId, long sessionId, DateTime at, decimal weight, params int[] reps) =>
        new(exerciseId, sessionId, at, reps.Select((r, i) => new PerformedSet(i + 1, weight, r, null, true)).ToList());

    private static ExerciseExposure ExposureRir(long exerciseId, long sessionId, DateTime at, decimal weight, int? rir, params int[] reps) =>
        new(exerciseId, sessionId, at, reps.Select((r, i) => new PerformedSet(i + 1, weight, r, rir, true)).ToList());

    private static readonly ProgressionInput Bench = new(TargetSets: 3, RepsMin: 6, RepsMax: 10, TargetRir: 2, IncrementKg: 2.5m, IsBodyweight: false, HasRecentDiscomfort: false);

    [Fact]
    public void Progression_NoHistory_Calibrates_WithoutWeight()
    {
        var d = ProgressionEngine.Decide(Bench, Array.Empty<ExerciseExposure>(), Options);
        Assert.Equal(ProgressionActions.Calibrate, d.Action);
        Assert.Null(d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_AllSetsAtTop_WithTargetRir_IncreasesBySmallEquipmentStep()
    {
        var d = ProgressionEngine.Decide(Bench, new[] { ExposureRir(1, 1, Now.AddDays(-3), 60m, 2, 10, 10, 10) }, Options);
        Assert.Equal(ProgressionActions.IncreaseWeight, d.Action);
        Assert.Equal(62.5m, d.SuggestedWeightKg);
        Assert.True(d.IsConfident);
    }

    [Fact]
    public void Progression_DumbbellIncrement_UsesDumbbellStep()
    {
        var input = Bench with { IncrementKg = 2m };
        var d = ProgressionEngine.Decide(input, new[] { ExposureRir(2, 1, Now.AddDays(-3), 20m, 3, 10, 10, 10) }, Options);
        Assert.Equal(22m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_TopReachedButRirMissing_IsNotAConfidentIncrease()
    {
        var d = ProgressionEngine.Decide(Bench, new[] { Exposure(1, 1, Now.AddDays(-3), 60m, 10, 10, 10) }, Options);
        Assert.Equal(ProgressionActions.ConsiderIncrease, d.Action);
        Assert.False(d.IsConfident);
    }

    [Fact]
    public void Progression_TopReachedButHarderThanTarget_KeepsWeight()
    {
        var d = ProgressionEngine.Decide(Bench, new[] { ExposureRir(1, 1, Now.AddDays(-3), 60m, 0, 10, 10, 10) }, Options);
        Assert.Equal(ProgressionActions.KeepWeight, d.Action);
        Assert.Equal(60m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_NotAllSetsAtTop_KeepsWeight_DoubleProgression()
    {
        var d = ProgressionEngine.Decide(Bench, new[] { ExposureRir(1, 1, Now.AddDays(-3), 60m, 2, 10, 10, 8) }, Options);
        Assert.Equal(ProgressionActions.KeepWeight, d.Action);
    }

    [Fact]
    public void Progression_IncompleteLog_MissingSets_NoIncrease()
    {
        // Hedef 3 set; yalnizca 2 set kaydedilmis ve biri tekrar bilgisi olmadan.
        var exposure = new ExerciseExposure(1, 1, Now.AddDays(-3), new[]
        {
            new PerformedSet(1, 60m, 10, 2, true),
            new PerformedSet(2, 60m, null, null, true),
        });
        var d = ProgressionEngine.Decide(Bench, new[] { exposure }, Options);
        Assert.Equal(ProgressionActions.KeepWeight, d.Action);
        Assert.Equal(60m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_FailedSet_BlocksIncrease_WarmupsIgnored()
    {
        var exposure = new ExerciseExposure(1, 1, Now.AddDays(-3), new[]
        {
            new PerformedSet(1, 40m, 10, 5, true, IsWarmup: true),
            new PerformedSet(2, 60m, 10, 2, true),
            new PerformedSet(3, 60m, 10, 2, true),
            new PerformedSet(4, 60m, 10, 2, IsCompleted: false),
        });
        var d = ProgressionEngine.Decide(Bench, new[] { exposure }, Options);
        Assert.Equal(ProgressionActions.KeepWeight, d.Action);
        Assert.Equal(60m, d.PreviousWeightKg);
    }

    [Fact]
    public void Progression_OneOffDrop_DoesNotChangeProgram()
    {
        var d = ProgressionEngine.Decide(Bench, new[]
        {
            Exposure(1, 1, Now.AddDays(-14), 60m, 10, 10, 10),
            Exposure(1, 2, Now.AddDays(-7), 60m, 10, 10, 10),
            Exposure(1, 3, Now.AddDays(-2), 60m, 6, 6, 6),
        }, Options);
        Assert.Equal(PerformanceTrend.OneOffDip, d.Trend);
        Assert.Equal(ProgressionActions.KeepWeight, d.Action);
        Assert.Equal(60m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_LastingDrop_ReducesLoadSlightly()
    {
        var d = ProgressionEngine.Decide(Bench, new[]
        {
            Exposure(1, 1, Now.AddDays(-14), 60m, 10, 10, 10),
            Exposure(1, 2, Now.AddDays(-7), 60m, 7, 7, 7),
            Exposure(1, 3, Now.AddDays(-2), 60m, 5, 5, 5),
        }, Options);
        Assert.Equal(PerformanceTrend.Declining, d.Trend);
        Assert.Equal(ProgressionActions.ReduceWeight, d.Action);
        Assert.Equal(55m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_ImprovingTrend_IsDetected()
    {
        var trend = ProgressionEngine.Trend(new[]
        {
            Exposure(1, 1, Now.AddDays(-21), 50m, 8, 8, 8),
            Exposure(1, 2, Now.AddDays(-14), 52.5m, 8, 8, 8),
            Exposure(1, 3, Now.AddDays(-7), 55m, 8, 8, 8),
        }, Options);
        Assert.Equal(PerformanceTrend.Improving, trend);
    }

    [Fact]
    public void Progression_Discomfort_HoldsLoad()
    {
        var d = ProgressionEngine.Decide(Bench with { HasRecentDiscomfort = true }, new[] { ExposureRir(1, 1, Now.AddDays(-3), 60m, 3, 10, 10, 10) }, Options);
        Assert.Equal(ProgressionActions.HoldDiscomfort, d.Action);
        Assert.Equal(60m, d.SuggestedWeightKg);
    }

    [Fact]
    public void Progression_Bodyweight_AdvancesReps()
    {
        var input = Bench with { IsBodyweight = true, IncrementKg = 0 };
        var exposure = new ExerciseExposure(4, 1, Now.AddDays(-3), Enumerable.Range(1, 3).Select(i => new PerformedSet(i, null, 10, 2, true)).ToList());
        var d = ProgressionEngine.Decide(input, new[] { exposure }, Options);
        Assert.Equal(ProgressionActions.IncreaseReps, d.Action);
        Assert.Null(d.SuggestedWeightKg);
    }

    [Fact]
    public void OneRepMax_IsEpleyEstimate_AndSkipsHighRepSets()
    {
        Assert.Equal(80m, LoadMath.EstimateOneRepMax(60m, 10));
        Assert.Null(LoadMath.EstimateOneRepMax(30m, 20));
    }

    // ---- Gelisim analizi ----

    [Fact]
    public async Task Analyzer_ImprovingExercise_ProducesDataBasedInsight()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var bench = TrainingTestCatalog.IdOf("Barbell Bench Press");
        var exposures = new Dictionary<long, IReadOnlyList<ExerciseExposure>>
        {
            [bench] = new[]
            {
                Exposure(bench, 1, Now.AddDays(-25), 50m, 8, 8, 8),
                Exposure(bench, 2, Now.AddDays(-18), 52.5m, 8, 8, 8),
                Exposure(bench, 3, Now.AddDays(-11), 55m, 8, 8, 8),
                Exposure(bench, 4, Now.AddDays(-4), 57.5m, 8, 8, 8),
            },
        };
        var analysis = PerformanceAnalyzer.Analyze(exposures, catalog, new TrainingHistory { ExposuresByExercise = exposures },
            new Dictionary<long, ProgressionDecision>(), Options, Now);

        var p = Assert.Single(analysis.Exercises);
        Assert.Equal(PerformanceTrend.Improving, p.Trend);
        Assert.Equal(4, analysis.CompletedSessions4Weeks);
        Assert.Contains(analysis.Insights, i => i.Type == "exercise_improved" && i.ExerciseId == bench);
        Assert.Contains(analysis.DataNotes, n => n.Contains("tahmin", StringComparison.OrdinalIgnoreCase));

        // Haftalik kas hacmi: gogus dogrudan 3 set; triceps / omuz yardimci 1.5 set.
        var week = PerformanceAnalyzer.WeekStart(Now.AddDays(-4));
        Assert.Equal(3m, analysis.RegionWeeks.Single(r => r.Region == MuscleRegion.Chest && r.WeekStart == week).Sets);
        Assert.Equal(1.5m, analysis.RegionWeeks.Single(r => r.Region == MuscleRegion.Triceps && r.WeekStart == week).Sets);
    }

    [Fact]
    public async Task Analyzer_NoData_ProducesNoComments()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var analysis = PerformanceAnalyzer.Analyze(new Dictionary<long, IReadOnlyList<ExerciseExposure>>(), catalog, TrainingHistory.Empty,
            new Dictionary<long, ProgressionDecision>(), Options, Now);

        Assert.Empty(analysis.Insights);
        Assert.Contains(analysis.DataNotes, n => n.Contains("yorumu üretilmedi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyzer_PoorAdherence_IsReported()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var history = new TrainingHistory { ScheduledSessionsLast4Weeks = 12, CompletedPlanSessionsLast4Weeks = 4 };
        var analysis = PerformanceAnalyzer.Analyze(new Dictionary<long, IReadOnlyList<ExerciseExposure>>(), catalog, history,
            new Dictionary<long, ProgressionDecision>(), Options, Now);

        Assert.Contains(analysis.Insights, i => i.Type == "low_adherence");
    }

    // ---- Toparlanma / deload ----

    private static Dictionary<long, IReadOnlyList<ExerciseExposure>> DecliningHistory(params long[] ids) => ids.ToDictionary(
        id => id,
        id => (IReadOnlyList<ExerciseExposure>)new[]
        {
            Exposure(id, 1, Now.AddDays(-12), 60m, 10, 10, 10),
            Exposure(id, 2, Now.AddDays(-7), 60m, 7, 7, 7),
            Exposure(id, 3, Now.AddDays(-2), 60m, 5, 5, 5),
        });

    private static FeedbackSignal Feedback(long sessionId, int fatigue, int recovery, int daysAgo, params long[] discomfort) =>
        new(sessionId, Now.AddDays(-daysAgo), 3, fatigue, recovery, 3, discomfort.Length > 0, discomfort);

    [Fact]
    public async Task Recovery_DeclinePlusFatigue_RecommendsDeload()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var history = new TrainingHistory
        {
            ExposuresByExercise = DecliningHistory(1, 12),
            RecentFeedback = new[] { Feedback(2, 5, 2, 7), Feedback(3, 4, 2, 2) },
        };
        var result = RecoveryAssessor.Assess(history, catalog, Options, Now);

        Assert.Equal(RecoveryStatus.DeloadRecommended, result.Status);
        Assert.True(result.SuggestedAdjustments.IsDeload);
    }

    [Fact]
    public async Task Recovery_DeclineWithoutFatigue_DoesNotDeload()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var history = new TrainingHistory
        {
            ExposuresByExercise = DecliningHistory(1, 12),
            RecentFeedback = new[] { Feedback(2, 2, 4, 7), Feedback(3, 2, 4, 2) },
        };
        var result = RecoveryAssessor.Assess(history, catalog, Options, Now);

        Assert.Equal(RecoveryStatus.Watch, result.Status);
        Assert.False(result.SuggestedAdjustments.IsDeload);
    }

    [Fact]
    public async Task Recovery_ManyWeeksOfGoodProgress_NoCalendarDeload()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var exposures = new Dictionary<long, IReadOnlyList<ExerciseExposure>>
        {
            [1] = Enumerable.Range(0, 10).Select(i => Exposure(1, i + 1, Now.AddDays(-70 + i * 7), 50m + i * 2.5m, 8, 8, 8)).ToList(),
            [12] = Enumerable.Range(0, 10).Select(i => Exposure(12, i + 1, Now.AddDays(-70 + i * 7), 80m + i * 5m, 8, 8, 8)).ToList(),
        };
        var result = RecoveryAssessor.Assess(new TrainingHistory
        {
            ExposuresByExercise = exposures,
            RecentFeedback = new[] { Feedback(9, 3, 4, 8), Feedback(10, 3, 4, 1) },
        }, catalog, Options, Now);

        Assert.Equal(RecoveryStatus.Good, result.Status);
        Assert.False(result.SuggestedAdjustments.IsDeload);
    }

    [Fact]
    public async Task Recovery_Discomfort_ExcludesExercise_WithSafeGuidance_NoDiagnosis()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var bench = TrainingTestCatalog.IdOf("Barbell Bench Press");
        var result = RecoveryAssessor.Assess(new TrainingHistory { RecentFeedback = new[] { Feedback(1, 3, 3, 3, bench) } }, catalog, Options, Now);

        Assert.Contains(bench, result.SuggestedAdjustments.ExcludedExerciseIds);
        var message = Assert.Single(result.Recommendations, r => r.Code == "discomfort").Message;
        Assert.Contains("sağlık profesyoneline", message, StringComparison.Ordinal);

        // Rahatsizlik bildirilen hareket yeni versiyona girmez; ayni bolgeden alternatif secilir.
        var (program, _) = await BuildAsync(Profile(ExperienceLevel.Intermediate, days: 4), adjustments: result.SuggestedAdjustments);
        Assert.DoesNotContain(program.Days.SelectMany(d => d.Exercises), e => e.Exercise.Id == bench);
        Assert.Contains(program.Days.SelectMany(d => d.Exercises), e => e.TargetRegion == MuscleRegion.Chest);
    }

    [Fact]
    public async Task Recovery_NoData_IsUnknown_AndListsGaps()
    {
        var catalog = await TrainingTestCatalog.LoadAsync(Options);
        var result = RecoveryAssessor.Assess(TrainingHistory.Empty, catalog, Options, Now);
        Assert.Equal(RecoveryStatus.Unknown, result.Status);
        Assert.Contains(result.DataGaps, g => g.Contains("Uyku", StringComparison.Ordinal));
    }

    // ---- AI yanit dogrulamasi ----

    private static readonly Dictionary<long, string> Names = new()
    {
        [1] = "Barbell Bench Press", [2] = "Dumbbell Incline Press", [12] = "Barbell Back Squat", [99] = "Bulgarian Split Squat",
    };

    [Fact]
    public void Coach_ValidAnswer_IsAccepted()
    {
        var r = CoachResponseValidator.Validate("""{"message":"Barbell Bench Press için 62.5 kg dene.","exerciseIds":[1]}""",
            new HashSet<long> { 1, 2 }, Names, new HashSet<decimal> { 60m, 62.5m });
        Assert.True(r.IsValid, r.Reason);
        Assert.Equal(new long[] { 1 }, r.ExerciseIds);
    }

    [Fact]
    public void Coach_ExerciseOutsideContext_IsRejected()
    {
        var byId = CoachResponseValidator.Validate("""{"message":"Bunu dene.","exerciseIds":[99]}""", new HashSet<long> { 1 }, Names, new HashSet<decimal>());
        var byName = CoachResponseValidator.Validate("""{"message":"Bulgarian Split Squat ekle.","exerciseIds":[]}""", new HashSet<long> { 1 }, Names, new HashSet<decimal>());
        Assert.False(byId.IsValid);
        Assert.False(byName.IsValid);
    }

    [Fact]
    public void Coach_InventedWeight_IsRejected()
    {
        var r = CoachResponseValidator.Validate("""{"message":"100 kg ile başla.","exerciseIds":[1]}""", new HashSet<long> { 1 }, Names, new HashSet<decimal> { 60m });
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Coach_Diagnosis_OrNonJson_IsRejected()
    {
        Assert.False(CoachResponseValidator.Validate("""{"message":"Bu bir menisküs yırtığı olabilir.","exerciseIds":[]}""", new HashSet<long>(), Names, new HashSet<decimal>()).IsValid);
        Assert.False(CoachResponseValidator.Validate("Serbest metin", new HashSet<long>(), Names, new HashSet<decimal>()).IsValid);
    }
}
