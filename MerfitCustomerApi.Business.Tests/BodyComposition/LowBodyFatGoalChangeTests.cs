using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.BodyComposition;

/// <summary>
/// Profilden Yag Kaybi hedefine geciste dusuk yag orani uyarisi sunucuda zorunludur: onaysiz istek
/// hicbir sey kaydetmeden 409 (LOW_BODY_FAT_CONFIRMATION_REQUIRED) doner; onayli istek hedefi kaydeder,
/// onay zamanini ve gecmis kaydini tutar ve plan yenilemeyi tetikler.
/// </summary>
public class LowBodyFatGoalChangeTests
{
    private const long UserId = 21;

    private sealed class Fixture
    {
        public required ProfileService Service { get; init; }
        public required UserProfile Profile { get; init; }
        public required List<UserGoal> Goals { get; init; }
        public required List<GoalHistory> History { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
        public required Mock<IUnitOfWork> UnitOfWork { get; init; }
    }

    private static Fixture Create(decimal? bodyFat, FitnessGoal currentGoal = FitnessGoal.BuildMuscle)
    {
        var profile = new UserProfile
        {
            Id = 1,
            UserId = UserId,
            FirstName = "Can",
            LastName = "Demir",
            Username = "can",
            WeightKg = 70,
            HeightCm = 180,
            DateOfBirth = DateTime.UtcNow.Date.AddYears(-27),
            Gender = Gender.Male,
            Goal = currentGoal,
            ExperienceLevel = ExperienceLevel.Beginner,
            TrainingDaysPerWeek = 3,
        };
        var measurements = bodyFat is null
            ? new List<BodyMeasurement>()
            : new List<BodyMeasurement> { new() { Id = 1, UserId = UserId, RecordedAt = DateTime.UtcNow.AddDays(-3), BodyFatPercentage = bodyFat } };
        var goals = new List<UserGoal> { new() { Id = 1, UserId = UserId, GoalType = currentGoal, IsActive = true } };
        var history = new List<GoalHistory>();
        var jobs = new List<PersonalizationJob>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(new List<ApplicationUser> { new() { Id = UserId, Email = "can@example.com" } }).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        unitOfWork.Setup(u => u.Repository<GoalHistory>()).Returns(MockRepositoryFactory.Create(history).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(measurements).Object);
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(new List<UserPreference>()).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(new List<UserNotificationSetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserPrivacySetting>()).Returns(MockRepositoryFactory.Create(new List<UserPrivacySetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(new List<UserEquipment>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(new List<WorkoutSession>()).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlan>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlanDay>()).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new Fixture
        {
            Service = new ProfileService(unitOfWork.Object),
            Profile = profile,
            Goals = goals,
            History = history,
            Jobs = jobs,
            UnitOfWork = unitOfWork,
        };
    }

    [Fact]
    public async Task ChangingToLoseWeight_WithLowBodyFat_WithoutConfirmation_IsRejectedAndNothingSaved()
    {
        var fx = Create(bodyFat: 6);

        var ex = await Assert.ThrowsAsync<LowBodyFatConfirmationRequiredException>(() =>
            fx.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "LOSE_WEIGHT" }));

        Assert.Equal(LowBodyFatConfirmationRequiredException.Code, "LOW_BODY_FAT_CONFIRMATION_REQUIRED");
        Assert.Contains("düşük aralıkta", ex.Message);
        Assert.Empty(fx.Jobs);
        Assert.Empty(fx.History);
        Assert.Equal(FitnessGoal.BuildMuscle, fx.Goals.Single(g => g.IsActive).GoalType);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangingToLoseWeight_WithLowBodyFat_AndConfirmation_SavesGoalAndAcknowledgement()
    {
        var fx = Create(bodyFat: 6);

        var response = await fx.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "LOSE_WEIGHT", ConfirmLowBodyFatWarning = true });

        Assert.Equal("LOSE_WEIGHT", response.Goal);
        var active = fx.Goals.Single(g => g.IsActive);
        Assert.Equal(FitnessGoal.LoseWeight, active.GoalType);
        Assert.NotNull(active.LowBodyFatWarningAcknowledgedAt);
        var history = Assert.Single(fx.History);
        Assert.Equal((FitnessGoal.BuildMuscle, FitnessGoal.LoseWeight), (history.OldGoal, history.NewGoal));
        Assert.False(Assert.Single(fx.Jobs).NutritionOnly);
    }

    [Theory]
    [InlineData(null)]   // olcum yok: sonuc uretilmez, uyari yok
    [InlineData(15.0)]   // saglikli aralik
    public async Task ChangingToLoseWeight_WithoutLowBodyFat_NeedsNoConfirmation(double? bodyFat)
    {
        var fx = Create(bodyFat: (decimal?)bodyFat);

        await fx.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "LOSE_WEIGHT" });

        Assert.Equal(FitnessGoal.LoseWeight, fx.Profile.Goal);
        Assert.Null(fx.Goals.Single(g => g.IsActive).LowBodyFatWarningAcknowledgedAt);
    }

    [Fact]
    public async Task OtherProfileEdits_DoNotTriggerTheWarning_ForExistingLoseWeightUser()
    {
        var fx = Create(bodyFat: 6, currentGoal: FitnessGoal.LoseWeight);

        await fx.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { FirstName = "Cem" });

        Assert.Equal("Cem", fx.Profile.FirstName);
    }

    [Fact]
    public async Task LegacyGoalCode_IsSavedAsPrimaryGoal()
    {
        var fx = Create(bodyFat: null, currentGoal: FitnessGoal.GetStronger);

        var response = await fx.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "MAINTAIN_WEIGHT" });

        Assert.Equal(FitnessGoal.BuildMuscle, fx.Profile.Goal);
        Assert.Equal("BUILD_MUSCLE", response.Goal);
    }
}
