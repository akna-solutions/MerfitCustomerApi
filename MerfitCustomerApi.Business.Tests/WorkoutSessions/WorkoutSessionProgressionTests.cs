using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutSessions;

/// <summary>
/// Plan gununden baslatilan oturumda egzersiz, tekrar araligini (8-12), son tamamlanan performansi ve
/// bir sonraki hedefi (cift progresyon) tasir. Yalnizca TAMAMLANMIS oturumlar dikkate alinir.
/// </summary>
public class WorkoutSessionProgressionTests
{
    private const long UserId = 3;
    private const long ExerciseId = 100;

    private sealed class Store
    {
        public List<WorkoutSession> Sessions { get; } = new();
        public List<WorkoutSessionExercise> SessionExercises { get; } = new();
        public List<WorkoutSetLog> SetLogs { get; } = new();

        public WorkoutSessionService CreateService()
        {
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(Sessions).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutSessionExercise>()).Returns(MockRepositoryFactory.Create(SessionExercises).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutSetLog>()).Returns(MockRepositoryFactory.Create(SetLogs).Object);
            unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(new List<Workout> { new() { Id = 1, Title = "Push", IsActive = true } }).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutPlanExercise>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlanExercise>
            {
                new() { Id = 1, WorkoutPlanDayId = 9, ExerciseId = ExerciseId, Order = 1, Sets = 3, Reps = 8, RepsMax = 12, RestSeconds = 90 },
            }).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutExercise>()).Returns(MockRepositoryFactory.Create(new List<WorkoutExercise>()).Object);
            unitOfWork.Setup(u => u.Repository<Exercise>()).Returns(MockRepositoryFactory.Create(new List<Exercise>
            {
                new() { Id = ExerciseId, Name = "Bench Press", EquipmentType = "Barbell" },
            }).Object);
            unitOfWork.Setup(u => u.Repository<PersonalRecord>()).Returns(MockRepositoryFactory.Create(new List<PersonalRecord>()).Object);

            return new WorkoutSessionService(unitOfWork.Object, Mock.Of<IMerfitScoreService>());
        }

        public void AddSession(long id, WorkoutSessionStatus status, DateTime startedAt, params int[] reps)
        {
            Sessions.Add(new WorkoutSession
            {
                Id = id,
                UserId = UserId,
                WorkoutId = 1,
                WorkoutPlanDayId = 9,
                Status = status,
                StartedAt = startedAt,
                CompletedAt = status == WorkoutSessionStatus.Completed ? startedAt.AddHours(1) : null,
            });
            var sessionExerciseId = id * 10;
            SessionExercises.Add(new WorkoutSessionExercise { Id = sessionExerciseId, WorkoutSessionId = id, ExerciseId = ExerciseId, Order = 1 });
            for (var i = 0; i < reps.Length; i++)
            {
                SetLogs.Add(new WorkoutSetLog { Id = id * 100 + i, WorkoutSessionExerciseId = sessionExerciseId, SetNumber = i + 1, WeightKg = 40m, Reps = reps[i] });
            }
        }
    }

    [Fact]
    public async Task CurrentSession_ShowsRepRange_PreviousPerformance_AndWeightIncrease()
    {
        var store = new Store();
        store.AddSession(1, WorkoutSessionStatus.Completed, DateTime.UtcNow.AddDays(-3), 12, 12, 12);
        store.AddSession(2, WorkoutSessionStatus.Started, DateTime.UtcNow);

        var dto = await store.CreateService().GetSessionAsync(UserId, 2);

        var exercise = Assert.Single(dto.Exercises);
        Assert.Equal(8, exercise.TargetReps);
        Assert.Equal(12, exercise.TargetRepsMax);
        Assert.Equal(3, exercise.LastPerformance!.Sets.Count);
        Assert.Equal("increase_weight", exercise.Progression!.Action);
        Assert.Equal(42m, exercise.Progression.SuggestedWeightKg);
    }

    [Fact]
    public async Task AbandonedSession_IsIgnored_ForProgression()
    {
        var store = new Store();
        store.AddSession(1, WorkoutSessionStatus.Completed, DateTime.UtcNow.AddDays(-5), 12, 11, 10);
        store.AddSession(2, WorkoutSessionStatus.Abandoned, DateTime.UtcNow.AddDays(-2), 12, 12, 12);
        store.AddSession(3, WorkoutSessionStatus.Started, DateTime.UtcNow);

        var dto = await store.CreateService().GetSessionAsync(UserId, 3);

        var exercise = Assert.Single(dto.Exercises);
        Assert.Equal(1, exercise.LastPerformance!.SessionId);
        Assert.Equal("keep_weight", exercise.Progression!.Action);
        Assert.Equal(40m, exercise.Progression.SuggestedWeightKg);
    }
}
