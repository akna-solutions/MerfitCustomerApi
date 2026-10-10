using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Configurations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Persistence;

/// <summary>
/// WorkoutAttributeScoreConfiguration'in tanimladigi veritabani kurallarini (benzersiz
/// WorkoutId+AttributeType, 0-100 check constraint, cascade silme) projenin gercek
/// IEntityTypeConfiguration siniflariyla in-memory SQLite uzerinde dogrular. (Ayni kurallar
/// PostgreSQL migration'inda da yer alir.)
///
/// Not: Tam AppDbContext modeli SQLite'ta kurulamiyor (AuditLog.MetadataJson "jsonb"), bu yuzden
/// yalnizca ilgili tablolarin konfigurasyonlarini uygulayan kucuk bir test baglami kullanilir.
/// </summary>
public sealed class WorkoutAttributeScorePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly long _workoutId;

    public WorkoutAttributeScorePersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();

        var category = new WorkoutCategory { Name = "Strength", Slug = "strength", IsActive = true, CreatedAt = DateTime.UtcNow };
        context.WorkoutCategories.Add(category);
        context.SaveChanges();

        var workout = new Workout
        {
            Title = "Full Body",
            Slug = "full-body",
            DurationMin = 30,
            Difficulty = DifficultyLevel.Beginner,
            CategoryId = category.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.Workouts.Add(workout);
        context.SaveChanges();
        _workoutId = workout.Id;
    }

    private sealed class WorkoutScoresTestDbContext : DbContext
    {
        public WorkoutScoresTestDbContext(DbContextOptions<WorkoutScoresTestDbContext> options) : base(options)
        {
        }

        public DbSet<WorkoutCategory> WorkoutCategories => Set<WorkoutCategory>();
        public DbSet<Workout> Workouts => Set<Workout>();
        public DbSet<WorkoutAttributeScore> WorkoutAttributeScores => Set<WorkoutAttributeScore>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WorkoutCategoryConfiguration());
            modelBuilder.ApplyConfiguration(new MuscleGroupConfiguration());
            modelBuilder.ApplyConfiguration(new WorkoutConfiguration());
            modelBuilder.ApplyConfiguration(new WorkoutAttributeScoreConfiguration());
        }
    }

    private WorkoutScoresTestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WorkoutScoresTestDbContext>().UseSqlite(_connection).Options);

    private WorkoutAttributeScore NewScore(WorkoutAttributeType type, int score) => new()
    {
        WorkoutId = _workoutId,
        AttributeType = type,
        Score = score,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void DuplicateAttributeTypeForSameWorkout_IsRejectedByDatabase()
    {
        using (var context = CreateContext())
        {
            context.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.Hypertrophy, 90));
            context.SaveChanges();
        }

        using var second = CreateContext();
        second.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.Hypertrophy, 40));

        var exception = Assert.Throws<DbUpdateException>(() => second.SaveChanges());
        Assert.Contains("UNIQUE", exception.InnerException!.Message);
    }

    [Fact]
    public void DifferentAttributeTypesForSameWorkout_AreAllowed()
    {
        using var context = CreateContext();
        foreach (var type in Enum.GetValues<WorkoutAttributeType>())
        {
            context.WorkoutAttributeScores.Add(NewScore(type, 50));
        }

        context.SaveChanges();

        Assert.Equal(6, context.WorkoutAttributeScores.Count(s => s.WorkoutId == _workoutId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ScoreOutsideZeroToHundred_IsRejectedByDatabase(int score)
    {
        using var context = CreateContext();
        context.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.Cardio, score));

        var exception = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.Contains("CK_WorkoutAttributeScore_Score_Range", exception.InnerException!.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void BoundaryScores_AreAccepted(int score)
    {
        using var context = CreateContext();
        context.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.Mobility, score));

        context.SaveChanges();

        Assert.Equal(score, context.WorkoutAttributeScores.Single().Score);
    }

    [Fact]
    public void AttributeType_IsStoredAsReadableText()
    {
        using (var context = CreateContext())
        {
            context.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.CoreStability, 70));
            context.SaveChanges();
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT AttributeType FROM WorkoutAttributeScore";
        Assert.Equal("CoreStability", command.ExecuteScalar());
    }

    [Fact]
    public void DeletingWorkout_CascadesToItsScores()
    {
        using (var context = CreateContext())
        {
            context.WorkoutAttributeScores.Add(NewScore(WorkoutAttributeType.Strength, 70));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Workouts.Remove(context.Workouts.Single(w => w.Id == _workoutId));
            context.SaveChanges();
        }

        using var verify = CreateContext();
        Assert.Empty(verify.WorkoutAttributeScores);
    }

    public void Dispose() => _connection.Dispose();
}
