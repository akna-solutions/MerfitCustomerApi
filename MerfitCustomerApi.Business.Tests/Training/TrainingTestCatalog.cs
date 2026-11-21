using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Training;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Tests.Training;

/// <summary>
/// Testler icin kucuk ama gercekci bir egzersiz veritabani. Uygulamadaki gibi bazi egzersizlerde metadata
/// (mekanik / patern / ekipman baglantisi) bos birakilir; motorun bunu tahmin etmesi ve raporlamasi beklenir.
/// Ekipman kimlikleri: 1 Barbell, 2 Dumbbell, 3 Machine, 4 Cable.
/// </summary>
public static class TrainingTestCatalog
{
    public const long Barbell = 1, Dumbbell = 2, Machine = 3, Cable = 4;

    public static readonly (long Id, string Name, string Slug)[] Muscles =
    {
        (1, "Göğüs", "chest"), (2, "Sırt", "back"), (3, "Omuz", "shoulders"), (4, "Biceps", "biceps"),
        (5, "Triceps", "triceps"), (6, "Ön Bacak", "quadriceps"), (7, "Arka Bacak", "hamstrings"),
        (8, "Kalça", "glutes"), (9, "Baldır", "calves"), (10, "Karın", "core"),
    };

    private sealed record Row(long Id, string Name, long Muscle, string? Equipment, long[] EquipmentIds, long[] Secondary,
        ExerciseMechanicType? Mechanic = null, MovementPattern? Pattern = null, DifficultyLevel Difficulty = DifficultyLevel.Beginner);

    private static readonly Row[] Rows =
    {
        new(1, "Barbell Bench Press", 1, "Barbell", new[] { Barbell }, new long[] { 5, 3 }, ExerciseMechanicType.Compound, MovementPattern.HorizontalPush, DifficultyLevel.Intermediate),
        new(2, "Dumbbell Incline Press", 1, "Dumbbell", new[] { Dumbbell }, new long[] { 5, 3 }),
        new(3, "Cable Fly", 1, "Cable", new[] { Cable }, Array.Empty<long>(), ExerciseMechanicType.Isolation),
        new(4, "Push-up", 1, "Bodyweight", Array.Empty<long>(), new long[] { 5, 3 }),
        new(5, "Barbell Row", 2, "Barbell", new[] { Barbell }, new long[] { 4 }, ExerciseMechanicType.Compound, MovementPattern.HorizontalPull, DifficultyLevel.Intermediate),
        new(6, "Lat Pulldown", 2, "Cable", new[] { Cable }, new long[] { 4 }, ExerciseMechanicType.Compound, MovementPattern.VerticalPull),
        new(7, "Dumbbell Row", 2, "Dumbbell", new[] { Dumbbell }, new long[] { 4 }),
        new(8, "Pull-up", 2, "Bodyweight", Array.Empty<long>(), new long[] { 4 }, ExerciseMechanicType.Compound, MovementPattern.VerticalPull, DifficultyLevel.Intermediate),
        new(9, "Barbell Overhead Press", 3, "Barbell", new[] { Barbell }, new long[] { 5 }, ExerciseMechanicType.Compound, MovementPattern.VerticalPush, DifficultyLevel.Intermediate),
        new(10, "Dumbbell Lateral Raise", 3, "Dumbbell", new[] { Dumbbell }, Array.Empty<long>()),
        new(11, "Dumbbell Shoulder Press", 3, "Dumbbell", new[] { Dumbbell }, new long[] { 5 }),
        new(12, "Barbell Back Squat", 6, "Barbell", new[] { Barbell }, new long[] { 8 }, ExerciseMechanicType.Compound, MovementPattern.Squat, DifficultyLevel.Intermediate),
        new(13, "Leg Press", 6, "Machine", new[] { Machine }, new long[] { 8 }, ExerciseMechanicType.Compound, MovementPattern.Squat),
        new(14, "Goblet Squat", 6, "Dumbbell", new[] { Dumbbell }, new long[] { 8 }),
        new(15, "Leg Extension", 6, "Machine", new[] { Machine }, Array.Empty<long>(), ExerciseMechanicType.Isolation),
        new(16, "Bodyweight Lunge", 6, "Bodyweight", Array.Empty<long>(), new long[] { 8 }),
        new(17, "Romanian Deadlift", 7, "Barbell", new[] { Barbell }, new long[] { 8, 2 }, ExerciseMechanicType.Compound, MovementPattern.Hinge, DifficultyLevel.Intermediate),
        new(18, "Lying Leg Curl", 7, "Machine", new[] { Machine }, Array.Empty<long>(), ExerciseMechanicType.Isolation),
        new(19, "Dumbbell Romanian Deadlift", 7, "Dumbbell", new[] { Dumbbell }, new long[] { 8 }),
        new(20, "Barbell Hip Thrust", 8, "Barbell", new[] { Barbell }, new long[] { 7 }, ExerciseMechanicType.Compound, MovementPattern.Hinge),
        new(21, "Glute Bridge", 8, "Bodyweight", Array.Empty<long>(), Array.Empty<long>()),
        new(22, "Barbell Curl", 4, "Barbell", new[] { Barbell }, Array.Empty<long>()),
        new(23, "Dumbbell Curl", 4, "Dumbbell", new[] { Dumbbell }, Array.Empty<long>()),
        new(24, "Cable Triceps Pushdown", 5, "Cable", new[] { Cable }, Array.Empty<long>()),
        new(25, "Dumbbell Overhead Triceps Extension", 5, "Dumbbell", new[] { Dumbbell }, Array.Empty<long>()),
        new(26, "Bench Dip", 5, "Bodyweight", Array.Empty<long>(), new long[] { 1 }),
        new(27, "Machine Calf Raise", 9, "Machine", new[] { Machine }, Array.Empty<long>()),
        new(28, "Dumbbell Calf Raise", 9, "Dumbbell", new[] { Dumbbell }, Array.Empty<long>()),
        new(29, "Plank", 10, "Bodyweight", Array.Empty<long>(), Array.Empty<long>()),
        new(30, "Crunch", 10, "Bodyweight", Array.Empty<long>(), Array.Empty<long>()),
    };

    public static int Count => Rows.Length;

    public static long IdOf(string name) => Rows.Single(r => r.Name == name).Id;

    /// <summary>Katalogu bellekteki birim calismaya yazar (Workout sablonlari dahil degil).</summary>
    public static InMemoryUnitOfWork Seed(InMemoryUnitOfWork uow)
    {
        uow.Set<MuscleGroup>().AddRange(Muscles.Select(m => new MuscleGroup { Id = m.Id, Name = m.Name, Slug = m.Slug }));
        uow.Set<Equipment>().AddRange(new[]
        {
            new Equipment { Id = Barbell, Name = "Barbell", Slug = "barbell" },
            new Equipment { Id = Dumbbell, Name = "Dumbbell", Slug = "dumbbell" },
            new Equipment { Id = Machine, Name = "Machine", Slug = "machine" },
            new Equipment { Id = Cable, Name = "Cable", Slug = "cable" },
        });
        foreach (var row in Rows)
        {
            uow.Set<Exercise>().Add(ToExercise(row));
            uow.Set<ExerciseMuscleGroup>().AddRange(row.Secondary.Select((m, i) => new ExerciseMuscleGroup
            {
                Id = row.Id * 10 + i, ExerciseId = row.Id, MuscleGroupId = m, Role = ExerciseMuscleRole.Secondary,
            }));
            uow.Set<ExerciseEquipment>().AddRange(row.EquipmentIds.Select((e, i) => new ExerciseEquipment
            {
                Id = row.Id * 10 + i, ExerciseId = row.Id, EquipmentId = e,
            }));
        }

        uow.Register<WorkoutExercise>();
        return uow;
    }

    public static Exercise ToExerciseEntity(long id) => ToExercise(Rows.Single(r => r.Id == id));

    public static IEnumerable<long> Ids => Rows.Select(r => r.Id);

    public static (long[] Secondary, long[] Equipment) LinksOf(long id)
    {
        var row = Rows.Single(r => r.Id == id);
        return (row.Secondary, row.EquipmentIds);
    }

    private static Exercise ToExercise(Row row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Slug = row.Name.ToLowerInvariant().Replace(' ', '-'),
        Difficulty = row.Difficulty,
        PrimaryMuscleGroupId = row.Muscle,
        EquipmentType = row.Equipment,
        IsActive = true,
        MechanicType = row.Mechanic,
        MovementPattern = row.Pattern,
    };

    public static async Task<IReadOnlyDictionary<long, CatalogExercise>> LoadAsync(TrainingEngineOptions? options = null)
    {
        var uow = Seed(new InMemoryUnitOfWork());
        return await TrainingHistoryReader.LoadCatalogAsync(uow.Object, options ?? new TrainingEngineOptions(), null, CancellationToken.None);
    }
}
