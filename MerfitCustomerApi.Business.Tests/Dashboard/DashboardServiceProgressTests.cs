using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Dashboard;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Dashboard;

/// <summary>
/// Dashboard'daki haftalik ilerleme karti: tamamlanan gun sayisi / haftalik hedef.
/// </summary>
public class DashboardServiceProgressTests
{
    private const long UserId = 3;

    private readonly List<WorkoutSession> _sessions = new();
    private readonly List<WorkoutPlan> _plans = new();
    private readonly List<WorkoutPlanDay> _planDays = new();

    private DashboardService CreateService(int? profileDays = 4)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile>
        {
            new() { Id = 1, UserId = UserId, FirstName = "Test", TrainingDaysPerWeek = profileDays },
        }).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(_sessions).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(_plans).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(_planDays).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(new List<UserGoal>()).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(new List<BodyMeasurement>()).Object);

        var workoutService = new Mock<IWorkoutService>();
        workoutService
            .Setup(w => w.GetWorkoutsAsync(UserId, It.IsAny<CustomerWorkoutListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<CustomerWorkoutListItemDto>.Create(new List<CustomerWorkoutListItemDto>(), 1, 5, 0));

        return new DashboardService(unitOfWork.Object, workoutService.Object);
    }

    private void AddCompletedSession(long id, DateTime completedAt) =>
        _sessions.Add(new WorkoutSession { Id = id, UserId = UserId, WorkoutId = 1, Status = WorkoutSessionStatus.Completed, CompletedAt = completedAt });

    [Fact]
    public async Task WeeklyProgress_CountsDistinctDays_NotSessions()
    {
        var now = DateTime.UtcNow;
        AddCompletedSession(1, now);
        AddCompletedSession(2, now.AddMinutes(-30)); // ayni gun ikinci antrenman

        var result = await CreateService().GetDashboardAsync(UserId);

        Assert.Equal(1, result.TodayProgress.WorkoutsCompleted);
    }

    [Fact]
    public async Task WeeklyTarget_FallsBackToProfileDays_WhenNoActivePlan()
    {
        var result = await CreateService(profileDays: 4).GetDashboardAsync(UserId);

        Assert.Equal(4, result.TodayProgress.WorkoutsTarget);
    }

    [Fact]
    public async Task WeeklyTarget_UsesActivePlanDayCount_WhenPlanExists()
    {
        _plans.Add(new WorkoutPlan { Id = 1, UserId = UserId, IsActive = true });
        _planDays.Add(new WorkoutPlanDay { Id = 1, WorkoutPlanId = 1, DayOfWeek = DayOfWeek.Monday, WorkoutId = 1 });
        _planDays.Add(new WorkoutPlanDay { Id = 2, WorkoutPlanId = 1, DayOfWeek = DayOfWeek.Wednesday, WorkoutId = 1 });
        _planDays.Add(new WorkoutPlanDay { Id = 3, WorkoutPlanId = 1, DayOfWeek = DayOfWeek.Friday, WorkoutId = 1 });

        var result = await CreateService(profileDays: 4).GetDashboardAsync(UserId);

        Assert.Equal(3, result.TodayProgress.WorkoutsTarget);
    }
}
