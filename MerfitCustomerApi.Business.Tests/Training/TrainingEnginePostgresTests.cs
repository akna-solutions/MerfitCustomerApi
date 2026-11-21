using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Training;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Training;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Opts = Microsoft.Extensions.Options.Options;

namespace MerfitCustomerApi.Business.Tests.Training;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Gercek PostgreSQL ile: katalog antrenmani olmayan motor gunleri
/// (WorkoutId null), ayni setin eszamanli iki kez kaydedilmesinde tek satir (benzersiz indeks + yeniden deneme),
/// RIR check constraint'i ve versiyon degisiminde eski plan / oturum / set gecmisinin korunmasi.
/// </summary>
public class TrainingEnginePostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static (TrainingProgramService Programs, WorkoutSessionService Sessions, AppDbContext Db) CreateServices()
    {
        var db = CreateContext();
        var uow = new UnitOfWork(db);
        var options = Opts.Create(new TrainingEngineOptions());
        return (new TrainingProgramService(uow, options), new WorkoutSessionService(uow, Mock.Of<IMerfitScoreService>(), engineOptions: options), db);
    }

    /// <summary>Test kullanicisi + (ilk calismada) kucuk katalog. Katalog adlari calismaya ozel son ek tasir.</summary>
    private static async Task<long> SeedAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var muscles = TrainingTestCatalog.Muscles.ToDictionary(m => m.Id, m => new MuscleGroup { Name = $"{m.Name} {suffix}", Slug = $"{m.Slug}-{suffix}", CreatedAt = DateTime.UtcNow });
        db.MuscleGroups.AddRange(muscles.Values);
        var equipment = new Dictionary<long, Equipment>
        {
            [TrainingTestCatalog.Barbell] = new() { Name = $"Barbell {suffix}", Slug = $"barbell-{suffix}", CreatedAt = DateTime.UtcNow },
            [TrainingTestCatalog.Dumbbell] = new() { Name = $"Dumbbell {suffix}", Slug = $"dumbbell-{suffix}", CreatedAt = DateTime.UtcNow },
            [TrainingTestCatalog.Machine] = new() { Name = $"Machine {suffix}", Slug = $"machine-{suffix}", CreatedAt = DateTime.UtcNow },
            [TrainingTestCatalog.Cable] = new() { Name = $"Cable {suffix}", Slug = $"cable-{suffix}", CreatedAt = DateTime.UtcNow },
        };
        db.Equipments.AddRange(equipment.Values);
        await db.SaveChangesAsync();

        foreach (var id in TrainingTestCatalog.Ids)
        {
            var exercise = TrainingTestCatalog.ToExerciseEntity(id);
            exercise.Id = 0;
            exercise.Slug = $"{exercise.Slug}-{suffix}";
            exercise.PrimaryMuscleGroupId = muscles[exercise.PrimaryMuscleGroupId].Id;
            exercise.CreatedAt = DateTime.UtcNow;
            db.Exercises.Add(exercise);
            await db.SaveChangesAsync();
            var (secondary, eq) = TrainingTestCatalog.LinksOf(id);
            db.ExerciseMuscleGroups.AddRange(secondary.Select(m => new ExerciseMuscleGroup { ExerciseId = exercise.Id, MuscleGroupId = muscles[m].Id, Role = ExerciseMuscleRole.Secondary, CreatedAt = DateTime.UtcNow }));
            db.ExerciseEquipments.AddRange(eq.Select(e => new ExerciseEquipment { ExerciseId = exercise.Id, EquipmentId = equipment[e].Id, CreatedAt = DateTime.UtcNow }));
        }

        var email = $"training-{suffix}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id, FirstName = "T", LastName = "U", Username = $"t{suffix}", ExperienceLevel = ExperienceLevel.Intermediate,
            TrainingDaysPerWeek = 4, WorkoutDurationMin = 60, TrainingLocation = TrainingLocation.Gym, HasEquipment = true, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    [PostgresFact]
    public async Task EngineProgram_Session_ConcurrentDuplicateSet_Versioning_PreservesHistory()
    {
        var userId = await SeedAsync();

        TrainingProgramDto v1;
        CustomerWorkoutSessionDto session;
        {
            var (programs, sessions, db) = CreateServices();
            await using (db)
            {
                v1 = await programs.GenerateAsync(userId, new GenerateTrainingProgramRequest { Focus = "Hypertrophy", DaysPerWeek = 4, SessionMinutes = 60 });
                session = await sessions.StartSessionAsync(userId, new StartWorkoutSessionRequest { WorkoutPlanDayId = v1.Days[0].WorkoutPlanDayId });
            }
        }

        Assert.Null(session.WorkoutId);
        var exercise = session.Exercises[0];

        // Ayni set eszamanli iki istekle gelir: benzersiz indeks tek satira izin verir, servis guncellemeye doner.
        async Task LogAsync(decimal weight)
        {
            var (_, sessions, db) = CreateServices();
            await using (db)
            {
                await sessions.LogSetAsync(userId, session.Id, exercise.SessionExerciseId, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = weight, Reps = 10, Rir = 2 });
            }
        }

        await Task.WhenAll(Task.Run(() => LogAsync(40m)), Task.Run(() => LogAsync(42.5m)));

        await using (var check = CreateContext())
        {
            var rows = await check.WorkoutSetLogs.Where(l => l.WorkoutSessionExerciseId == exercise.SessionExerciseId).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Contains(row.WeightKg, new decimal?[] { 40m, 42.5m });
            Assert.True(row.IsCompleted);
            Assert.False(row.IsWarmup);

            // RIR check constraint.
            row.Rir = 11;
            await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
        }

        TrainingProgramDto v2;
        {
            var (programs, sessions, db) = CreateServices();
            await using (db)
            {
                await sessions.CompleteSessionAsync(userId, session.Id, new CompleteWorkoutSessionRequest { DurationSeconds = 1800 });
                await sessions.SubmitFeedbackAsync(userId, session.Id, new SubmitWorkoutFeedbackRequest { Difficulty = 3, Fatigue = 3, Recovery = 4, Satisfaction = 4 });
                var proposal = await programs.GenerateAsync(userId, new GenerateTrainingProgramRequest { Focus = "Strength", DaysPerWeek = 3, SessionMinutes = 60 });
                Assert.Equal("Proposed", proposal.Status);
                v2 = await programs.AcceptProposalAsync(userId, proposal.Id);
            }
        }

        await using (var check = CreateContext())
        {
            var plans = await check.Set<WorkoutPlan>().Where(p => p.UserId == userId).OrderBy(p => p.ProgramVersion).ToListAsync();
            Assert.Equal(new[] { TrainingProgramStatus.Archived, TrainingProgramStatus.Active }, plans.Select(p => p.Status));
            Assert.Equal(v2.Id, plans[1].Id);
            Assert.Equal(plans[0].Id, plans[1].PreviousPlanId);

            var stored = await check.WorkoutSessions.SingleAsync(s => s.Id == session.Id);
            Assert.Equal(WorkoutSessionStatus.Completed, stored.Status);
            Assert.Equal(v1.Days[0].WorkoutPlanDayId, stored.WorkoutPlanDayId);
            Assert.True(await check.Set<WorkoutPlanDay>().AnyAsync(d => d.Id == stored.WorkoutPlanDayId && d.WorkoutPlanId == v1.Id));
            Assert.Equal(1, await check.WorkoutSetLogs.CountAsync(l => l.WorkoutSessionExerciseId == exercise.SessionExerciseId));
            Assert.Equal(1, await check.Set<WorkoutSessionFeedback>().CountAsync(f => f.WorkoutSessionId == session.Id));
        }

        // Eski versiyonun gecmisi ilerleme analizinde gorunmeye devam eder.
        {
            var (programs, _, db) = CreateServices();
            await using (db)
            {
                var progress = await programs.GetProgressAsync(userId);
                Assert.Equal(1, progress.CompletedSessions4Weeks);
            }
        }
    }
}
