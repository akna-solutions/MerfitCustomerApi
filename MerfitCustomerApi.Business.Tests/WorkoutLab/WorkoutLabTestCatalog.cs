using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.WorkoutLab;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Tests.WorkoutLab;

/// <summary>
/// Antrenman Laboratuvari testleri icin bellekte katalog. Gercek WorkoutPlanGenerator ve
/// WorkoutPlanSimulationProcessor bu katalog uzerinde calistirilir (mock generator yok).
/// </summary>
public sealed class WorkoutLabTestCatalog
{
    public const long Dumbbell = 1;
    public const long Barbell = 2;
    public const long Bench = 3;
    public const long PullUpBar = 4;
    public const long Bike = 5;

    public InMemoryUnitOfWork Uow { get; } = new();

    public WorkoutLabTestCatalog()
    {
        // Kullanici tablolari bilincli olarak kaydedilir; testler bunlara hic erisilmedigini dogrular.
        Uow.Register<UserProfile>().Register<UserGoal>().Register<UserEquipment>().Register<Subscription>()
            .Register<WorkoutPlan>().Register<WorkoutPlanDay>().Register<WorkoutPlanExercise>()
            .Register<PersonalizationJob>().Register<NotificationDispatch>();

        var equipment = Uow.Set<Equipment>();
        equipment.Add(new Equipment { Id = Dumbbell, Name = "Dumbbell", Slug = "dumbbell" });
        equipment.Add(new Equipment { Id = Barbell, Name = "Barbell", Slug = "barbell" });
        equipment.Add(new Equipment { Id = Bench, Name = "Bench", Slug = "bench" });
        equipment.Add(new Equipment { Id = PullUpBar, Name = "Pull-up Bar", Slug = "pull-up-bar" });
        equipment.Add(new Equipment { Id = Bike, Name = "Stationary Bike", Slug = "bike" });

        var muscles = Uow.Set<MuscleGroup>();
        foreach (var (id, name) in new[] { (1L, "Göğüs"), (2L, "Sırt"), (3L, "Bacak"), (4L, "Omuz"), (5L, "Tüm Vücut"), (6L, "Kardiyo") })
        {
            muscles.Add(new MuscleGroup { Id = id, Name = name, Slug = name.ToLowerInvariant() });
        }

        Uow.Set<Workout>();
        Uow.Set<WorkoutEquipment>();
        Uow.Set<WorkoutExercise>();
        Uow.Set<Exercise>();
        Uow.Set<WorkoutAttributeScore>();
        Uow.Set<WorkoutPlanSimulation>();
    }

    public List<WorkoutPlanSimulation> Simulations => Uow.Set<WorkoutPlanSimulation>();

    /// <summary>Gercekci bir varsayilan katalog: bodyweight, dumbbell, barbell/bench antrenmanlari ve kardiyolar.</summary>
    public WorkoutLabTestCatalog WithDefaultCatalog()
    {
        // Bodyweight (ekipmansiz)
        Workout(101, "BW Üst Vücut", DifficultyLevel.Beginner, 30, muscle: 1, scores: (75, 40, 20));
        Workout(102, "BW Bacak", DifficultyLevel.Beginner, 30, muscle: 3, scores: (70, 45, 25));
        Workout(103, "BW Tüm Vücut", DifficultyLevel.Beginner, 25, muscle: 5, scores: (60, 35, 45));
        Workout(104, "BW Core & Sırt", DifficultyLevel.Intermediate, 35, muscle: 2, scores: (55, 40, 30));
        // Dumbbell
        Workout(201, "DB Göğüs", DifficultyLevel.Beginner, 40, muscle: 1, equipment: new[] { Dumbbell }, scores: (85, 50, 10));
        Workout(202, "DB Sırt", DifficultyLevel.Intermediate, 45, muscle: 2, equipment: new[] { Dumbbell }, scores: (80, 55, 10));
        Workout(203, "DB Omuz", DifficultyLevel.Intermediate, 40, muscle: 4, equipment: new[] { Dumbbell }, scores: (80, 45, 10));
        // Barbell + bench (salon)
        Workout(301, "Barbell Bench Press Günü", DifficultyLevel.Intermediate, 60, muscle: 1, equipment: new[] { Barbell, Bench }, scores: (60, 90, 5));
        Workout(302, "Squat Günü", DifficultyLevel.Intermediate, 60, muscle: 3, equipment: new[] { Barbell }, scores: (65, 95, 5));
        Workout(303, "Deadlift & Sırt", DifficultyLevel.Intermediate, 55, muscle: 2, equipment: new[] { Barbell, PullUpBar }, scores: (60, 90, 5));
        Workout(304, "İleri Seviye Push", DifficultyLevel.Advanced, 70, muscle: 1, equipment: new[] { Barbell, Bench, Dumbbell }, scores: (85, 75, 10));
        Workout(305, "İleri Seviye Pull", DifficultyLevel.Advanced, 70, muscle: 2, equipment: new[] { Barbell, PullUpBar }, scores: (85, 75, 10));
        Workout(306, "İleri Seviye Bacak", DifficultyLevel.Advanced, 75, muscle: 3, equipment: new[] { Barbell }, scores: (85, 80, 10));
        // Kardiyo
        Workout(401, "HIIT 15", DifficultyLevel.Beginner, 15, muscle: 6, scores: (10, 5, 90), exerciseCount: 3);
        Workout(402, "Bisiklet 20", DifficultyLevel.Intermediate, 20, muscle: 6, equipment: new[] { Bike }, scores: (5, 5, 95), exerciseCount: 1);
        Workout(403, "İleri HIIT 20", DifficultyLevel.Advanced, 20, muscle: 6, scores: (15, 10, 90), exerciseCount: 4);
        return this;
    }

    public Workout Workout(
        long id,
        string title,
        DifficultyLevel difficulty,
        int durationMin,
        long muscle,
        long[]? equipment = null,
        (int H, int S, int C)? scores = null,
        bool premium = false,
        bool active = true,
        int exerciseCount = 5)
    {
        var workout = new Workout
        {
            Id = id, Title = title, Slug = $"w-{id}", Difficulty = difficulty, DurationMin = durationMin,
            MuscleGroupId = muscle, IsPremium = premium, IsActive = active, CategoryId = 1,
        };
        Uow.Set<Workout>().Add(workout);

        foreach (var e in equipment ?? Array.Empty<long>())
        {
            Uow.Set<WorkoutEquipment>().Add(new WorkoutEquipment { Id = id * 10 + e, WorkoutId = id, EquipmentId = e });
        }

        for (var i = 1; i <= exerciseCount; i++)
        {
            var exerciseId = id * 100 + i;
            Uow.Set<Exercise>().Add(new Exercise
            {
                Id = exerciseId, Name = $"{title} Egzersiz {i}", Slug = $"e-{exerciseId}", PrimaryMuscleGroupId = muscle,
                EquipmentType = equipment is { Length: > 0 } ? "Ekipmanlı" : "Vücut ağırlığı", IsActive = true,
            });
            Uow.Set<WorkoutExercise>().Add(new WorkoutExercise
            {
                Id = exerciseId, WorkoutId = id, ExerciseId = exerciseId, Order = i, Sets = 3, Reps = 12, RestSeconds = 45,
                DurationSeconds = muscle == 6 ? 60 : null,
            });
        }

        if (scores is { } s)
        {
            var list = Uow.Set<WorkoutAttributeScore>();
            list.Add(new WorkoutAttributeScore { Id = list.Count + 1, WorkoutId = id, AttributeType = WorkoutAttributeType.Hypertrophy, Score = s.H });
            list.Add(new WorkoutAttributeScore { Id = list.Count + 1, WorkoutId = id, AttributeType = WorkoutAttributeType.Strength, Score = s.S });
            list.Add(new WorkoutAttributeScore { Id = list.Count + 1, WorkoutId = id, AttributeType = WorkoutAttributeType.Cardio, Score = s.C });
        }

        return workout;
    }

    public WorkoutPlanGenerator CreateGenerator(WorkoutPlanGenerationOptions? options = null) =>
        new(Uow.Object, Options.Create(options ?? new WorkoutPlanGenerationOptions()), NullLogger<WorkoutPlanGenerator>.Instance);

    public WorkoutPlanSimulationProcessor CreateProcessor(WorkoutPlanLabOptions? labOptions = null, WorkoutPlanGenerationOptions? options = null)
    {
        var generationOptions = options ?? new WorkoutPlanGenerationOptions();
        return new WorkoutPlanSimulationProcessor(
            Uow.Object,
            CreateGenerator(generationOptions),
            Options.Create(generationOptions),
            Options.Create(labOptions ?? new WorkoutPlanLabOptions()),
            NullLogger<WorkoutPlanSimulationProcessor>.Instance);
    }

    public WorkoutPlanSimulation Enqueue(WorkoutPlanSimulationProfile profile)
    {
        var simulation = new WorkoutPlanSimulation
        {
            Id = Simulations.Count + 1,
            Status = WorkoutPlanSimulationStatus.Pending,
            ProfileJson = JsonSerializer.Serialize(profile, WorkoutLabJson.Options),
            Goal = profile.Goal ?? FitnessGoal.BuildMuscle,
            ExperienceLevel = profile.ExperienceLevel ?? ExperienceLevel.Beginner,
            TrainingDaysPerWeek = profile.TrainingDays.Count > 0 ? profile.TrainingDays.Count : profile.TrainingDaysPerWeek ?? 3,
            WorkoutDurationMin = profile.WorkoutDurationMin,
            TrainingLocation = profile.TrainingLocation,
            IsPlus = profile.IsPlus,
            CreatedAt = DateTime.UtcNow,
        };
        Simulations.Add(simulation);
        return simulation;
    }

    /// <summary>Profili kuyruga ekler, isler ve sonucu doner.</summary>
    public async Task<(WorkoutPlanSimulation Simulation, WorkoutPlanSimulationResult? Result, List<WorkoutPlanQualityCheck> Checks)> RunAsync(
        WorkoutPlanSimulationProfile profile,
        WorkoutPlanLabOptions? labOptions = null,
        WorkoutPlanGenerationOptions? options = null)
    {
        var simulation = Enqueue(profile);
        var processed = await CreateProcessor(labOptions, options).ProcessNextAsync();
        if (!processed)
        {
            throw new InvalidOperationException("Simulasyon islenmedi.");
        }

        var stored = Simulations.Single(s => s.Id == simulation.Id);
        var result = stored.ResultJson is null ? null : JsonSerializer.Deserialize<WorkoutPlanSimulationResult>(stored.ResultJson, WorkoutLabJson.Options);
        var checks = stored.QualityChecksJson is null
            ? new List<WorkoutPlanQualityCheck>()
            : JsonSerializer.Deserialize<List<WorkoutPlanQualityCheck>>(stored.QualityChecksJson, WorkoutLabJson.Options)!;
        return (stored, result, checks);
    }
}
