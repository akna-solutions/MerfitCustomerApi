using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutSessions;

/// <summary>
/// Antrenman kayit akisi: set dogrulamasi (0 kg), bos oturumun tamamlanamamasi, "geçen sefer"
/// verisi, antrenman gecmisi ve bugunun plan antrenmaninin tamamlandi bilgisi.
/// </summary>
public class WorkoutSessionServiceTests
{
    private const long UserId = 5;
    private const long BenchExerciseId = 100;
    private const long PushUpExerciseId = 200;

    private readonly List<Workout> _workouts = new()
    {
        new() { Id = 1, Title = "Göğüs Günü", Slug = "gogus", IsActive = true, DurationMin = 45 },
    };
    private readonly List<WorkoutExercise> _workoutExercises = new()
    {
        new() { Id = 1, WorkoutId = 1, ExerciseId = BenchExerciseId, Order = 1, Sets = 3, Reps = 10, RestSeconds = 60 },
        new() { Id = 2, WorkoutId = 1, ExerciseId = PushUpExerciseId, Order = 2, Sets = 3, Reps = 15, RestSeconds = 45 },
    };
    private readonly List<Exercise> _exercises = new()
    {
        new() { Id = BenchExerciseId, Name = "Bench Press", EquipmentType = "Barbell", IsActive = true },
        new() { Id = PushUpExerciseId, Name = "Şınav", EquipmentType = "Bodyweight", IsActive = true },
    };
    private readonly List<WorkoutSession> _sessions = new();
    private readonly List<WorkoutSessionExercise> _sessionExercises = new();
    private readonly List<WorkoutSetLog> _setLogs = new();
    private readonly List<WorkoutPlan> _plans = new();
    private readonly List<WorkoutPlanDay> _planDays = new();

    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public WorkoutSessionServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(_workouts).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutExercise>()).Returns(MockRepositoryFactory.Create(_workoutExercises).Object);
        _unitOfWork.Setup(u => u.Repository<Exercise>()).Returns(MockRepositoryFactory.Create(_exercises).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(_sessions).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutSessionExercise>()).Returns(MockRepositoryFactory.Create(_sessionExercises).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutSetLog>()).Returns(MockRepositoryFactory.Create(_setLogs).Object);
        _unitOfWork.Setup(u => u.Repository<PersonalRecord>()).Returns(MockRepositoryFactory.Create(new List<PersonalRecord>()).Object);
        _unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(_plans).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(_planDays).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutPlanExercise>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlanExercise>()).Object);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private WorkoutSessionService CreateService() => new(_unitOfWork.Object);

    /// <summary>Aktif (Started) bir oturum ve iki egzersiz kaydi ekler; bench = 11, sinav = 12.</summary>
    private WorkoutSession SeedActiveSession(long sessionId = 10)
    {
        var session = new WorkoutSession { Id = sessionId, UserId = UserId, WorkoutId = 1, StartedAt = DateTime.UtcNow, Status = WorkoutSessionStatus.Started };
        _sessions.Add(session);
        _sessionExercises.Add(new WorkoutSessionExercise { Id = sessionId + 1, WorkoutSessionId = sessionId, ExerciseId = BenchExerciseId, Order = 1 });
        _sessionExercises.Add(new WorkoutSessionExercise { Id = sessionId + 2, WorkoutSessionId = sessionId, ExerciseId = PushUpExerciseId, Order = 2 });
        return session;
    }

    private void SeedCompletedSession(long sessionId, DateTime completedAt, params (long ExerciseId, decimal? Weight, int Reps)[] sets)
    {
        _sessions.Add(new WorkoutSession
        {
            Id = sessionId, UserId = UserId, WorkoutId = 1, StartedAt = completedAt.AddMinutes(-40),
            CompletedAt = completedAt, DurationSeconds = 2400, Status = WorkoutSessionStatus.Completed,
        });
        var setNumber = 0;
        foreach (var group in sets.GroupBy(s => s.ExerciseId))
        {
            var sessionExerciseId = sessionId * 100 + group.Key;
            _sessionExercises.Add(new WorkoutSessionExercise { Id = sessionExerciseId, WorkoutSessionId = sessionId, ExerciseId = group.Key });
            var n = 0;
            foreach (var set in group)
            {
                _setLogs.Add(new WorkoutSetLog { Id = sessionId * 1000 + ++setNumber, WorkoutSessionExerciseId = sessionExerciseId, SetNumber = ++n, WeightKg = set.Weight, Reps = set.Reps });
            }
        }
    }

    [Fact]
    public async Task LogSet_RejectsZeroWeight_ForWeightedExercise()
    {
        SeedActiveSession();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().LogSetAsync(UserId, 10, 11, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = 0, Reps = 10 }));

        Assert.Contains("WeightKg", ex.Errors.Keys);
        Assert.Empty(_setLogs);
    }

    [Fact]
    public async Task LogSet_RejectsMissingWeight_ForWeightedExercise()
    {
        SeedActiveSession();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().LogSetAsync(UserId, 10, 11, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = null, Reps = 10 }));
    }

    [Fact]
    public async Task LogSet_AllowsNoWeight_ForBodyweightExercise_AndStoresNull()
    {
        SeedActiveSession();

        var result = await CreateService().LogSetAsync(UserId, 10, 12, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = 0, Reps = 15 });

        var log = Assert.Single(_setLogs);
        Assert.Null(log.WeightKg);
        Assert.Equal(15, log.Reps);
        Assert.False(result.RequiresWeight);
    }

    [Fact]
    public async Task LogSet_StoresPositiveWeight_ForWeightedExercise()
    {
        SeedActiveSession();

        var result = await CreateService().LogSetAsync(UserId, 10, 11, new LogWorkoutSetRequest { SetNumber = 1, WeightKg = 42.5m, Reps = 8 });

        Assert.Equal(42.5m, Assert.Single(_setLogs).WeightKg);
        Assert.True(result.RequiresWeight);
    }

    [Fact]
    public async Task Complete_WithoutAnySet_IsRejected_AndSessionStaysActive()
    {
        var session = SeedActiveSession();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateService().CompleteSessionAsync(UserId, 10, new CompleteWorkoutSessionRequest { DurationSeconds = 60 }));

        Assert.Equal(WorkoutSessionStatus.Started, session.Status);
    }

    [Fact]
    public async Task Complete_WithLoggedSet_MarksSessionCompleted()
    {
        var session = SeedActiveSession();
        _setLogs.Add(new WorkoutSetLog { Id = 1, WorkoutSessionExerciseId = 11, SetNumber = 1, WeightKg = 40, Reps = 10 });

        await CreateService().CompleteSessionAsync(UserId, 10, new CompleteWorkoutSessionRequest { DurationSeconds = 600 });

        Assert.Equal(WorkoutSessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task StartSession_ReturnsLastPerformance_FromMostRecentCompletedSession()
    {
        SeedCompletedSession(1, DateTime.UtcNow.AddDays(-7), (BenchExerciseId, 30m, 10));
        SeedCompletedSession(2, DateTime.UtcNow.AddDays(-2), (BenchExerciseId, 40m, 10), (BenchExerciseId, 45m, 8));

        var dto = await CreateService().StartSessionAsync(UserId, new StartWorkoutSessionRequest { WorkoutId = 1 });

        var bench = dto.Exercises.Single(e => e.ExerciseId == BenchExerciseId);
        Assert.True(bench.RequiresWeight);
        Assert.NotNull(bench.LastPerformance);
        Assert.Equal(2, bench.LastPerformance!.SessionId);
        Assert.Equal(new decimal?[] { 40m, 45m }, bench.LastPerformance.Sets.Select(s => s.WeightKg).ToArray());

        var pushUp = dto.Exercises.Single(e => e.ExerciseId == PushUpExerciseId);
        Assert.False(pushUp.RequiresWeight);
        Assert.Null(pushUp.LastPerformance);
    }

    [Fact]
    public async Task GetHistory_ReturnsOnlyCompletedSessions_NewestFirst_WithVolume()
    {
        SeedCompletedSession(1, DateTime.UtcNow.AddDays(-3), (BenchExerciseId, 40m, 10));
        SeedCompletedSession(2, DateTime.UtcNow.AddDays(-1), (BenchExerciseId, 50m, 5), (BenchExerciseId, 60m, 5), (PushUpExerciseId, null, 20));
        SeedActiveSession(sessionId: 30); // tamamlanmamis: gecmiste gorunmemeli
        _sessions.Add(new WorkoutSession { Id = 40, UserId = 999, WorkoutId = 1, Status = WorkoutSessionStatus.Completed, CompletedAt = DateTime.UtcNow }); // baska kullanici

        var page = await CreateService().GetHistoryAsync(UserId, new PagedRequest { Page = 1, PageSize = 20 });

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new long[] { 2, 1 }, page.Items.Select(i => i.Id).ToArray());
        var latest = page.Items[0];
        Assert.Equal("Göğüs Günü", latest.Title);
        Assert.Equal(3, latest.SetCount);
        Assert.Equal(2, latest.ExerciseCount);
        Assert.Equal(550m, latest.TotalVolumeKg);
        Assert.Equal(60m, latest.MaxWeightKg);
    }

    [Fact]
    public async Task MyPlanToday_ReportsCompletedToday_WhenPlanDaySessionWasCompletedToday()
    {
        _plans.Add(new WorkoutPlan { Id = 1, UserId = UserId, IsActive = true });
        _planDays.Add(new WorkoutPlanDay { Id = 7, WorkoutPlanId = 1, WorkoutId = 1, DayOfWeek = DateTime.UtcNow.DayOfWeek });
        var service = new MyPlanService(_unitOfWork.Object);

        var before = await service.GetTodayAsync(UserId);
        Assert.True(before.HasWorkoutToday);
        Assert.False(before.IsCompletedToday);

        _sessions.Add(new WorkoutSession { Id = 50, UserId = UserId, WorkoutId = 1, WorkoutPlanDayId = 7, Status = WorkoutSessionStatus.Completed, CompletedAt = DateTime.UtcNow });

        var after = await service.GetTodayAsync(UserId);
        Assert.True(after.IsCompletedToday);
    }
}
