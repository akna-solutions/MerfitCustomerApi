using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Progress;

/// <summary>Otomatik hedef kilo, gunluk kilo girisi ve kilo hedefi ilerlemesi.</summary>
public class WeightProgressTests
{
    private const long UserId = 5;

    // ---- TargetWeightCalculator ----

    [Theory]
    [InlineData(FitnessGoal.LoseWeight, 95, 180, ExperienceLevel.Beginner, 71.5)]   // ideal: BMI 22
    [InlineData(FitnessGoal.LoseWeight, 68, 180, ExperienceLevel.Beginner, 66)]     // zaten ideal alti: -2 kg
    [InlineData(FitnessGoal.LoseWeight, 61, 180, ExperienceLevel.Beginner, 60)]     // BMI 18.5 altina inmez
    [InlineData(FitnessGoal.BuildMuscle, 70, 180, ExperienceLevel.Beginner, 74)]    // +%6
    [InlineData(FitnessGoal.BuildMuscle, 70, 180, ExperienceLevel.Advanced, 71)]    // +%1.5
    [InlineData(FitnessGoal.BuildMuscle, 55, 180, ExperienceLevel.Beginner, 60)]    // zayifsa en az BMI 18.5
    [InlineData(FitnessGoal.GetStronger, 80, 180, ExperienceLevel.Beginner, 82.5)]  // +%3
    [InlineData(FitnessGoal.MaintainWeight, 80.3, 180, ExperienceLevel.Beginner, 85)]      // eski hedef -> Kas Gelistirme
    [InlineData(FitnessGoal.ImproveEndurance, 77, 170, ExperienceLevel.Beginner, 81.5)]  // eski hedef -> Kas Gelistirme
    public void TargetWeight_DependsOnGoalBodyAndExperience(FitnessGoal goal, double weight, double height, ExperienceLevel level, double expected)
    {
        Assert.Equal((decimal)expected, TargetWeightCalculator.Calculate(goal, (decimal)weight, (decimal)height, level));
    }

    [Fact]
    public void TargetWeight_IsNull_WhenHeightOrWeightUnknown()
    {
        Assert.Null(TargetWeightCalculator.Calculate(FitnessGoal.LoseWeight, 90, null, null));
        Assert.Null(TargetWeightCalculator.Calculate(FitnessGoal.LoseWeight, null, 180, null));
    }

    [Theory]
    [InlineData(95, 90, 71.5, 21)]
    [InlineData(95, 97, 71.5, 0)]    // ters yone gitti
    [InlineData(95, 70, 71.5, 100)]  // hedefi gecti
    [InlineData(70, 72, 74, 50)]     // kas kazanimi
    [InlineData(80, 80.8, 80, 100)]  // koruma: 1 kg icinde
    [InlineData(80, 83, 80, 60)]     // koruma: 3 kg uzaklasti
    public void ProgressPercent_FollowsDirectionOfGoal(double start, double current, double target, int expected)
    {
        Assert.Equal(expected, WeightTracking.CalculateProgressPercent((decimal)start, (decimal)current, (decimal)target));
    }

    // ---- ProgressService.LogWeightAsync ----

    [Fact]
    public async Task LogWeight_SameDayTwice_KeepsSingleEntry()
    {
        var f = new ProgressFixture(MakeProfile(weight: 90));

        await f.Service.LogWeightAsync(UserId, new LogWeightRequest { WeightKg = 89.6m });
        await f.Service.LogWeightAsync(UserId, new LogWeightRequest { WeightKg = 89.4m });

        var entry = Assert.Single(f.Measurements);
        Assert.Equal(89.4m, entry.WeightKg);
        Assert.Equal(DateTime.UtcNow.Date, entry.RecordedAt.Date);
        Assert.NotNull(entry.BMI);
    }

    [Fact]
    public async Task LogWeight_SmallChange_DoesNotTouchNutritionInputs()
    {
        var f = new ProgressFixture(MakeProfile(weight: 90));
        f.Goals.Add(new UserGoal { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 90, TargetWeightKg = 71.5m });

        await f.Service.LogWeightAsync(UserId, new LogWeightRequest { WeightKg = 89.4m });

        Assert.Equal(90m, f.Profiles[0].WeightKg);
        Assert.Empty(f.Jobs);
        Assert.Equal(89.4m, f.Goals[0].CurrentWeightKg);
    }

    [Fact]
    public async Task LogWeight_OneKiloChange_UpdatesProfileAndQueuesNutritionPlan()
    {
        var f = new ProgressFixture(MakeProfile(weight: 90));

        await f.Service.LogWeightAsync(UserId, new LogWeightRequest { WeightKg = 88.8m });

        Assert.Equal(88.8m, f.Profiles[0].WeightKg);
        var job = Assert.Single(f.Jobs);
        Assert.True(job.NutritionOnly);
    }

    [Fact]
    public async Task LogWeight_BackfilledDay_DoesNotChangeCurrentWeight()
    {
        var f = new ProgressFixture(MakeProfile(weight: 90));
        f.Goals.Add(new UserGoal { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 92, CurrentWeightKg = 90, TargetWeightKg = 71.5m });
        f.Measurements.Add(new BodyMeasurement { Id = 1, UserId = UserId, RecordedAt = DateTime.UtcNow.Date, WeightKg = 90 });

        await f.Service.LogWeightAsync(UserId, new LogWeightRequest { WeightKg = 85m, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)) });

        Assert.Equal(2, f.Measurements.Count);
        Assert.Equal(90m, f.Goals[0].CurrentWeightKg);
        Assert.Equal(90m, f.Profiles[0].WeightKg);
    }

    [Fact]
    public async Task LogWeight_RejectsFutureDates()
    {
        var f = new ProgressFixture(MakeProfile(weight: 90));

        await Assert.ThrowsAsync<AppValidationException>(() => f.Service.LogWeightAsync(
            UserId, new LogWeightRequest { WeightKg = 80, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) }));
        Assert.Empty(f.Measurements);
    }

    // ---- ProgressService.GetProgressAsync ----

    [Fact]
    public async Task Progress_FillsMissingTargetFromGoal_AndReportsPercent()
    {
        var f = new ProgressFixture(MakeProfile(weight: 95));
        f.Goals.Add(new UserGoal { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 95 });
        f.Measurements.Add(new BodyMeasurement { Id = 1, UserId = UserId, RecordedAt = DateTime.UtcNow.Date.AddDays(-10), WeightKg = 95 });
        f.Measurements.Add(new BodyMeasurement { Id = 2, UserId = UserId, RecordedAt = DateTime.UtcNow.Date, WeightKg = 90 });

        var result = await f.Service.GetProgressAsync(UserId, "month");

        Assert.Equal(71.5m, result.TargetWeight);
        Assert.Equal(71.5m, f.Goals[0].TargetWeightKg);
        Assert.Equal(71.5m, f.Profiles[0].TargetWeightKg);
        Assert.Equal(90m, result.CurrentWeight);
        Assert.Equal(90m, result.TodayWeight);
        Assert.Equal(21m, result.GoalPercent);
        Assert.Equal("lose", result.WeightDirection);
        Assert.Equal(-5m, result.MonthlyChange);
        Assert.Equal(2, result.WeightHistory.Count);
    }

    [Fact]
    public async Task Progress_MuscleGoal_TracksWeightGain()
    {
        var f = new ProgressFixture(MakeProfile(weight: 70, goal: FitnessGoal.BuildMuscle));
        f.Goals.Add(new UserGoal { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.BuildMuscle, StartingWeightKg = 70 });
        f.Measurements.Add(new BodyMeasurement { Id = 1, UserId = UserId, RecordedAt = DateTime.UtcNow.Date, WeightKg = 72 });

        var result = await f.Service.GetProgressAsync(UserId, "month");

        Assert.Equal(74m, result.TargetWeight);
        Assert.Equal("gain", result.WeightDirection);
        Assert.Equal(50m, result.GoalPercent);
        Assert.Null(result.WorkoutsGoal);
    }

    // ---- ProfileService: hedef degisince hedef kilo yeniden hesaplanir ----

    [Fact]
    public async Task ChangingGoal_RecalculatesTargetAndRestartsFromCurrentWeight()
    {
        var profile = MakeProfile(weight: 80, goal: FitnessGoal.LoseWeight);
        var goals = new List<UserGoal> { new() { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 90, TargetWeightKg = 71.5m } };
        var service = CreateProfileService(profile, goals, new List<BodyMeasurement>());

        await service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "BUILD_MUSCLE" });

        Assert.Equal(FitnessGoal.BuildMuscle, goals[0].GoalType);
        Assert.Equal(80m, goals[0].StartingWeightKg);
        Assert.Equal(85m, goals[0].TargetWeightKg); // 80 * 1.06 = 84.8 -> 85
        Assert.Equal(85m, profile.TargetWeightKg);
    }

    [Fact]
    public async Task ChangingGoal_WithManualTarget_KeepsUsersTarget()
    {
        var profile = MakeProfile(weight: 80, goal: FitnessGoal.LoseWeight);
        var goals = new List<UserGoal> { new() { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 90, TargetWeightKg = 71.5m } };
        var service = CreateProfileService(profile, goals, new List<BodyMeasurement>());

        await service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "BUILD_MUSCLE", TargetWeight = 83 });

        Assert.Single(goals);
        Assert.Equal(83m, goals[0].TargetWeightKg);
    }

    [Fact]
    public async Task ProfileWeightUpdate_IsAddedToWeightHistory()
    {
        var profile = MakeProfile(weight: 80);
        var goals = new List<UserGoal> { new() { Id = 1, UserId = UserId, IsActive = true, GoalType = FitnessGoal.LoseWeight, StartingWeightKg = 80, TargetWeightKg = 71.5m } };
        var measurements = new List<BodyMeasurement>();
        var service = CreateProfileService(profile, goals, measurements);

        await service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Weight = 78.5m });

        Assert.Equal(78.5m, Assert.Single(measurements).WeightKg);
        Assert.Equal(78.5m, goals[0].CurrentWeightKg);
        Assert.Equal(71.5m, goals[0].TargetWeightKg);
    }

    // ---- helpers ----

    private static UserProfile MakeProfile(decimal weight, FitnessGoal goal = FitnessGoal.LoseWeight) => new()
    {
        Id = 1,
        UserId = UserId,
        FirstName = "Ayse",
        Username = "ayse",
        WeightKg = weight,
        HeightCm = 180,
        Goal = goal,
        ExperienceLevel = ExperienceLevel.Beginner,
        TrainingDaysPerWeek = 3,
    };

    private sealed class ProgressFixture
    {
        public List<UserProfile> Profiles { get; }
        public List<UserGoal> Goals { get; } = new();
        public List<BodyMeasurement> Measurements { get; } = new();
        public List<PersonalizationJob> Jobs { get; } = new();
        public ProgressService Service { get; }

        public ProgressFixture(UserProfile profile)
        {
            Profiles = new List<UserProfile> { profile };
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(Profiles).Object);
            unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(Goals).Object);
            unitOfWork.Setup(u => u.Repository<GoalHistory>()).Returns(MockRepositoryFactory.Create(new List<GoalHistory>()).Object);
            unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(Measurements).Object);
            unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(Jobs).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(new List<WorkoutSession>()).Object);
            unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
            unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(new List<Workout>()).Object);
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var score = new Mock<IMerfitScoreService>();
            score.Setup(s => s.RankUsersAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MerfitRankedUser>());
            score.Setup(s => s.GetPointEventsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MerfitPointEvent>());

            Service = new ProgressService(unitOfWork.Object, score.Object);
        }
    }

    private static ProfileService CreateProfileService(UserProfile profile, List<UserGoal> goals, List<BodyMeasurement> measurements)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(new List<ApplicationUser> { new() { Id = UserId, Email = "a@b.com" } }).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        unitOfWork.Setup(u => u.Repository<GoalHistory>()).Returns(MockRepositoryFactory.Create(new List<GoalHistory>()).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(measurements).Object);
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(new List<UserPreference>()).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(new List<UserNotificationSetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserPrivacySetting>()).Returns(MockRepositoryFactory.Create(new List<UserPrivacySetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(new List<UserEquipment>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(new List<WorkoutSession>()).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlan>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlanDay>()).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(new List<PersonalizationJob>()).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ProfileService(unitOfWork.Object);
    }
}
