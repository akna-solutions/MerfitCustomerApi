using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.WorkoutLab;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutLab;

/// <summary>
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES) uctan uca: admin API'sinin yazdigi gibi Pending bir simulasyon
/// gercek UnitOfWork ve gercek WorkoutPlanGenerator ile islenir; jsonb kolonlari okunabilir; ayni kaydi iki
/// isleyici ayni anda alamaz; kullanici tablolarinda (plan, is, profil) hicbir satir olusmaz.
/// </summary>
public class WorkoutPlanSimulationPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static WorkoutPlanSimulationProcessor CreateProcessor(AppDbContext db)
    {
        var uow = new UnitOfWork(db);
        var options = Options.Create(new WorkoutPlanGenerationOptions());
        return new WorkoutPlanSimulationProcessor(
            uow,
            new WorkoutPlanGenerator(uow, options, NullLogger<WorkoutPlanGenerator>.Instance),
            options,
            Options.Create(new WorkoutPlanLabOptions()),
            NullLogger<WorkoutPlanSimulationProcessor>.Instance);
    }

    private static async Task<long> SeedWorkoutAsync(string prefix)
    {
        await using var db = CreateContext();
        var category = new WorkoutCategory { Name = $"{prefix}-cat", Slug = $"{prefix}-cat", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.WorkoutCategories.Add(category);
        var muscle = new MuscleGroup { Name = $"{prefix}-mg", Slug = $"{prefix}-mg", CreatedAt = DateTime.UtcNow };
        db.MuscleGroups.Add(muscle);
        await db.SaveChangesAsync();

        var workout = new Workout
        {
            Title = $"{prefix} BW", Slug = $"{prefix}-bw", DurationMin = 20, Difficulty = DifficultyLevel.Beginner,
            CategoryId = category.Id, MuscleGroupId = muscle.Id, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        db.Workouts.Add(workout);
        var exercise = new Exercise { Name = $"{prefix} Şınav", Slug = $"{prefix}-sinav", PrimaryMuscleGroupId = muscle.Id, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync();
        db.WorkoutExercises.Add(new WorkoutExercise { WorkoutId = workout.Id, ExerciseId = exercise.Id, Order = 1, Sets = 3, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return workout.Id;
    }

    private static async Task<long> EnqueueAsync(string name)
    {
        await using var db = CreateContext();
        var profile = new WorkoutPlanSimulationProfile
        {
            Goal = FitnessGoal.BuildMuscle, ExperienceLevel = ExperienceLevel.Beginner, NoEquipment = true, TrainingDaysPerWeek = 3, WorkoutDurationMin = 45,
        };
        var sim = new WorkoutPlanSimulation
        {
            Name = name, Status = WorkoutPlanSimulationStatus.Pending, ProfileJson = JsonSerializer.Serialize(profile, WorkoutLabJson.Options),
            Goal = FitnessGoal.BuildMuscle, ExperienceLevel = ExperienceLevel.Beginner, TrainingDaysPerWeek = 3, WorkoutDurationMin = 45,
            TrainingLocation = TrainingLocation.Home, CreatedAt = DateTime.UtcNow,
        };
        db.WorkoutPlanSimulations.Add(sim);
        await db.SaveChangesAsync();
        return sim.Id;
    }

    [PostgresFact]
    public async Task PendingSimulation_IsProcessedWithRealGenerator_AndTouchesNoUserData()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var prefix = $"lab-{Guid.NewGuid():N}"[..20];
        await SeedWorkoutAsync(prefix);
        int plansBefore, jobsBefore, profilesBefore;
        await using (var db = CreateContext())
        {
            plansBefore = await db.WorkoutPlans.CountAsync();
            jobsBefore = await db.PersonalizationJobs.CountAsync();
            profilesBefore = await db.UserProfiles.CountAsync();
        }

        var id = await EnqueueAsync(prefix);

        // Kuyrukta baska testlerin kayitlari olabilir; bizimki islenene kadar isle.
        for (var i = 0; i < 50; i++)
        {
            await using var db = CreateContext();
            var status = await db.WorkoutPlanSimulations.Where(s => s.Id == id).Select(s => s.Status).FirstAsync();
            if (status is WorkoutPlanSimulationStatus.Completed or WorkoutPlanSimulationStatus.Failed)
            {
                break;
            }

            await CreateProcessor(db).ProcessNextAsync();
        }

        await using (var db = CreateContext())
        {
            var sim = await db.WorkoutPlanSimulations.SingleAsync(s => s.Id == id);
            Assert.Equal(WorkoutPlanSimulationStatus.Completed, sim.Status);
            Assert.Equal(WorkoutPlanGenerator.AlgorithmVersion, sim.AlgorithmVersion);
            var result = JsonSerializer.Deserialize<WorkoutPlanSimulationResult>(sim.ResultJson!, WorkoutLabJson.Options)!;
            Assert.Equal(3, result.Days.Count);
            Assert.True(sim.PassedCheckCount + sim.WarningCheckCount + sim.FailedCheckCount == 10);

            // jsonb sorgulanabilir (admin listesi bu kolonlari oldugu gibi okur).
            var dayCount = await db.Database
                .SqlQuery<int>($"SELECT jsonb_array_length(\"ResultJson\"->'days') AS \"Value\" FROM workout.\"WorkoutPlanSimulation\" WHERE \"Id\" = {id}")
                .SingleAsync();
            Assert.Equal(3, dayCount);

            Assert.Equal(plansBefore, await db.WorkoutPlans.CountAsync());
            Assert.Equal(jobsBefore, await db.PersonalizationJobs.CountAsync());
            Assert.Equal(profilesBefore, await db.UserProfiles.CountAsync());
        }
    }

    [PostgresFact]
    public async Task ConcurrentProcessors_NeverProcessTheSameSimulationTwice()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var prefix = $"lab-{Guid.NewGuid():N}"[..20];
        await SeedWorkoutAsync(prefix);
        var ids = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await EnqueueAsync($"{prefix}-{i}"));
        }

        async Task DrainAsync()
        {
            for (var i = 0; i < 20; i++)
            {
                await using var db = CreateContext();
                if (!await CreateProcessor(db).ProcessNextAsync())
                {
                    return;
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DrainAsync)));

        await using var check = CreateContext();
        var sims = await check.WorkoutPlanSimulations.Where(s => ids.Contains(s.Id)).ToListAsync();
        Assert.All(sims, s => Assert.Equal(WorkoutPlanSimulationStatus.Completed, s.Status));
        Assert.All(sims, s => Assert.Equal(1, s.AttemptCount));
    }
}
