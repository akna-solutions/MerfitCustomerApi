using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>
/// GET/PUT /api/profile'daki antrenman alanlarinin uydurma degil, kayitta verilen bilgilerden ve
/// aktif programdan geldigini dogrular (antrenman gunleri, antrenman yeri, hedef kilo).
/// </summary>
public class ProfileServiceTrainingDataTests
{
    private const long UserId = 7;

    private sealed class Fixture
    {
        public required ProfileService Service { get; init; }
        public required List<UserProfile> Profiles { get; init; }
        public required List<UserGoal> Goals { get; init; }
        public required List<UserEquipment> Equipment { get; init; }
        public required List<WorkoutPlan> Plans { get; init; }
        public required List<WorkoutPlanDay> PlanDays { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
    }

    private static Fixture CreateFixture(UserProfile profile)
    {
        var users = new List<ApplicationUser> { new() { Id = UserId, Email = "ali@example.com" } };
        var profiles = new List<UserProfile> { profile };
        var goals = new List<UserGoal>();
        var equipment = new List<UserEquipment>();
        var plans = new List<WorkoutPlan>();
        var planDays = new List<WorkoutPlanDay>();
        var jobs = new List<PersonalizationJob>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(users).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(profiles).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(new List<UserPreference>()).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(new List<UserNotificationSetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserPrivacySetting>()).Returns(MockRepositoryFactory.Create(new List<UserPrivacySetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(equipment).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(new List<WorkoutSession>()).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(plans).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(planDays).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new Fixture
        {
            Service = new ProfileService(unitOfWork.Object),
            Profiles = profiles,
            Goals = goals,
            Equipment = equipment,
            Plans = plans,
            PlanDays = planDays,
            Jobs = jobs,
        };
    }

    private static UserProfile MakeProfile() => new()
    {
        Id = 1,
        UserId = UserId,
        FirstName = "Ali",
        LastName = "Yilmaz",
        Username = "ali",
        WeightKg = 82,
        TrainingDaysPerWeek = 3,
        TrainingLocation = TrainingLocation.Gym,
        Goal = FitnessGoal.BuildMuscle,
        ExperienceLevel = ExperienceLevel.Beginner,
    };

    [Fact]
    public async Task GetProfileAsync_ReturnsActivePlanDays_SortedMondayFirst()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Plans.Add(new WorkoutPlan { Id = 10, UserId = UserId, IsActive = false, StartDate = DateTime.UtcNow.AddDays(-30) });
        fixture.Plans.Add(new WorkoutPlan { Id = 11, UserId = UserId, IsActive = true, StartDate = DateTime.UtcNow });
        fixture.PlanDays.Add(new WorkoutPlanDay { WorkoutPlanId = 10, DayOfWeek = DayOfWeek.Tuesday });
        fixture.PlanDays.Add(new WorkoutPlanDay { WorkoutPlanId = 11, DayOfWeek = DayOfWeek.Sunday });
        fixture.PlanDays.Add(new WorkoutPlanDay { WorkoutPlanId = 11, DayOfWeek = DayOfWeek.Friday });
        fixture.PlanDays.Add(new WorkoutPlanDay { WorkoutPlanId = 11, DayOfWeek = DayOfWeek.Monday });

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Equal(new[] { "Mon", "Fri", "Sun" }, result.TrainingDays);
        Assert.Equal(3, result.TrainingDaysPerWeek);
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsNoDays_WhenNoActivePlan_InsteadOfInventingThem()
    {
        var fixture = CreateFixture(MakeProfile());

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Empty(result.TrainingDays);
        Assert.Equal(3, result.TrainingDaysPerWeek);
    }

    [Fact]
    public async Task GetProfileAsync_GymUserWithoutEquipmentRows_IsFullGym()
    {
        var fixture = CreateFixture(MakeProfile());

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Equal("FULL_GYM", result.Equipment);
        Assert.Equal("gym", result.TrainingLocation);
    }

    [Fact]
    public async Task GetProfileAsync_HomeUserWithoutEquipment_IsBodyweight()
    {
        var profile = MakeProfile();
        profile.TrainingLocation = TrainingLocation.Home;
        var fixture = CreateFixture(profile);

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Equal("BODYWEIGHT", result.Equipment);
        Assert.Equal("home", result.TrainingLocation);
    }

    [Fact]
    public async Task GetProfileAsync_TargetWeightIsZero_WhenNeverSet()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Goals.Add(new UserGoal { UserId = UserId, IsActive = true, TargetWeightKg = null });

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Equal(0, result.TargetWeight);
        Assert.Equal(82, result.Weight);
    }

    [Fact]
    public async Task UpdateProfileAsync_PersistsFrequencyAndLocation()
    {
        var fixture = CreateFixture(MakeProfile());

        var result = await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest
        {
            TrainingDaysPerWeek = 5,
            TrainingLocation = "outdoor",
        });

        Assert.Equal(5, fixture.Profiles[0].TrainingDaysPerWeek);
        Assert.Equal(TrainingLocation.Outdoor, fixture.Profiles[0].TrainingLocation);
        Assert.Equal(5, result.TrainingDaysPerWeek);
        Assert.Equal("outdoor", result.TrainingLocation);
    }

    [Fact]
    public async Task UpdateProfileAsync_EmptyLegacyDayList_DoesNotWipeFrequency()
    {
        var fixture = CreateFixture(MakeProfile());

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TrainingDays = new List<string>() });

        Assert.Equal(3, fixture.Profiles[0].TrainingDaysPerWeek);
    }

    [Fact]
    public async Task UpdateProfileAsync_TargetWeight_IsStoredOnGoalAndProfile()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Goals.Add(new UserGoal { UserId = UserId, IsActive = true });

        var result = await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TargetWeight = 75 });

        Assert.Equal(75, fixture.Goals[0].TargetWeightKg);
        Assert.Equal(75, fixture.Profiles[0].TargetWeightKg);
        Assert.Equal(75, result.TargetWeight);
    }

    [Fact]
    public async Task UpdateProfileAsync_SelectedDays_AreStoredAndReturned_AndCountFollows()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Plans.Add(new WorkoutPlan { Id = 11, UserId = UserId, IsActive = true, StartDate = DateTime.UtcNow });
        fixture.PlanDays.Add(new WorkoutPlanDay { WorkoutPlanId = 11, DayOfWeek = DayOfWeek.Monday });

        var result = await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest
        {
            TrainingDays = new List<string> { "sat", "Tue", "Thu", "Tue" },
        });

        Assert.Equal("Tue,Thu,Sat", fixture.Profiles[0].PreferredTrainingDays);
        Assert.Equal(3, fixture.Profiles[0].TrainingDaysPerWeek);
        Assert.Equal(new[] { "Tue", "Thu", "Sat" }, result.TrainingDays);
        Assert.Equal(3, result.TrainingDaysPerWeek);
    }

    [Fact]
    public async Task UpdateProfileAsync_InvalidDayCode_IsRejected()
    {
        var fixture = CreateFixture(MakeProfile());

        await Assert.ThrowsAsync<MerfitCustomerApi.Domain.Exceptions.AppValidationException>(() =>
            fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TrainingDays = new List<string> { "Mon", "Funday" } }));
        Assert.Null(fixture.Profiles[0].PreferredTrainingDays);
    }

    [Fact]
    public async Task UpdateProfileAsync_FrequencyOnly_ClearsMismatchedDaySelection()
    {
        var profile = MakeProfile();
        profile.PreferredTrainingDays = "Mon,Wed,Fri";
        var fixture = CreateFixture(profile);

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TrainingDaysPerWeek = 4 });

        Assert.Equal(4, fixture.Profiles[0].TrainingDaysPerWeek);
        Assert.Null(fixture.Profiles[0].PreferredTrainingDays);
    }

    [Fact]
    public async Task UpdateProfileAsync_GoalChange_UpdatesActiveGoal_AndQueuesPlanRegeneration()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Goals.Add(new UserGoal { UserId = UserId, IsActive = true, GoalType = FitnessGoal.BuildMuscle });

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "LOSE_WEIGHT" });

        Assert.Equal(FitnessGoal.LoseWeight, fixture.Profiles[0].Goal);
        Assert.Equal(FitnessGoal.LoseWeight, fixture.Goals[0].GoalType);
        var job = Assert.Single(fixture.Jobs);
        Assert.Equal(UserId, job.UserId);
        Assert.Equal(PersonalizationJobStatus.Pending, job.Status);
    }

    [Fact]
    public async Task UpdateProfileAsync_DayChange_QueuesPlanRegeneration()
    {
        var fixture = CreateFixture(MakeProfile());

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TrainingDays = new List<string> { "Mon", "Wed", "Fri" } });

        Assert.Single(fixture.Jobs);
    }

    [Fact]
    public async Task UpdateProfileAsync_SameValues_DoNotQueuePlanRegeneration()
    {
        var fixture = CreateFixture(MakeProfile());

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest
        {
            Goal = "BUILD_MUSCLE",
            ExperienceLevel = "BEGINNER",
            TrainingDaysPerWeek = 3,
            TrainingLocation = "gym",
            // Kilo degisikligi artik beslenme programini yeniden urettigi icin (bkz.
            // ProfileServiceNutritionPreferenceTests) burada mevcut kilo gonderilir.
            Weight = 82,
        });

        Assert.Empty(fixture.Jobs);
    }

    [Fact]
    public async Task UpdateProfileAsync_DoesNotQueueSecondJob_WhileOneIsPending()
    {
        var fixture = CreateFixture(MakeProfile());
        fixture.Jobs.Add(new PersonalizationJob { UserId = UserId, Status = PersonalizationJobStatus.Pending });

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "LOSE_WEIGHT" });

        Assert.Single(fixture.Jobs);
    }

    [Fact]
    public async Task GetProfileAsync_Age_IsExactOnBirthday_EvenForLeapYearBirths()
    {
        var today = DateTime.UtcNow.Date;
        var profile = MakeProfile();
        // Artik yil dogumlu, dogum gunu bugun: DayOfYear karsilastirmasi burada bir eksik veriyordu.
        var birthYear = today.Year - 30;
        while (!DateTime.IsLeapYear(birthYear)) birthYear--;
        var age = today.Year - birthYear;
        profile.DateOfBirth = DateTime.SpecifyKind(new DateTime(birthYear, today.Month, Math.Min(today.Day, DateTime.DaysInMonth(birthYear, today.Month))), DateTimeKind.Utc);
        var fixture = CreateFixture(profile);

        var result = await fixture.Service.GetProfileAsync(UserId);

        Assert.Equal(today.Day == profile.DateOfBirth.Value.Day ? age : age - 1, result.Age);
    }

    [Fact]
    public async Task UpdateProfileAsync_DateOfBirth_IsStoredAsUtc()
    {
        var fixture = CreateFixture(MakeProfile());

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { DateOfBirth = "1996-03-15" });

        var stored = fixture.Profiles[0].DateOfBirth!.Value;
        Assert.Equal(new DateTime(1996, 3, 15), stored);
        // Npgsql "timestamp with time zone" kolonuna yalnizca Kind=Utc yazabiliyor.
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }
}
