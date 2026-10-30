using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>
/// Yalnizca alisveris icin kayit olmus kullanicinin MB Fit'i actiginda fitness profilini tamamlamasi
/// (POST /api/profile/fitness): kayittaki kurallar uygulanir ve kisisel plan kuyruga alinir.
/// </summary>
public class FitnessProfileServiceTests
{
    private const long UserId = 7;

    private sealed class Fixture
    {
        public required FitnessProfileService Service { get; init; }
        public required UserProfile Profile { get; init; }
        public required List<UserGoal> Goals { get; init; }
        public required List<NutritionGoal> NutritionGoals { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
        public required List<BodyMeasurement> Measurements { get; init; }
        public required List<UserEquipment> Equipment { get; init; }
        public required Mock<IUnitOfWork> UnitOfWork { get; init; }
    }

    private static Fixture CreateFixture(decimal? heightCm = null)
    {
        var profile = new UserProfile { Id = 1, UserId = UserId, FirstName = "Ayşe", LastName = "Yılmaz", Username = "ayse", HeightCm = heightCm };
        var goals = new List<UserGoal>();
        var nutritionGoals = new List<NutritionGoal>();
        var jobs = new List<PersonalizationJob>();
        var measurements = new List<BodyMeasurement>();
        var equipment = new List<UserEquipment>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        unitOfWork.Setup(u => u.Repository<NutritionGoal>()).Returns(MockRepositoryFactory.Create(nutritionGoals).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(measurements).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(equipment).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var nutritionCalculator = new Mock<INutritionCalculator>();
        nutritionCalculator.Setup(n => n.Calculate(It.IsAny<UserProfile>(), It.IsAny<BodyFatAssessment?>()))
            .Returns(new NutritionCalculationResult { DailyCalories = 2100, ProteinTarget = 120, CarbsTarget = 230, FatTarget = 70, WaterTargetMl = 2400 });

        return new Fixture
        {
            Service = new FitnessProfileService(unitOfWork.Object, nutritionCalculator.Object),
            Profile = profile,
            Goals = goals,
            NutritionGoals = nutritionGoals,
            Jobs = jobs,
            Measurements = measurements,
            Equipment = equipment,
            UnitOfWork = unitOfWork,
        };
    }

    private static FitnessProfileRequest MakeRequest() => new()
    {
        Gender = "female",
        Age = 30,
        HeightUnit = "cm",
        HeightCm = 165,
        WeightUnit = "kg",
        Weight = 62,
        Goal = "build_muscle",
        ActivityLevel = "moderate",
        TrainingExperience = "beginner",
        TrainingDays = 3,
        TrainingLocation = "home",
        EquipmentIds = new List<long> { 4, 4, 9 },
    };

    [Fact]
    public async Task CompleteAsync_FillsProfile_CreatesGoalsAndQueuesPlan()
    {
        var fx = CreateFixture();

        await fx.Service.CompleteAsync(UserId, MakeRequest());

        Assert.Equal(165m, fx.Profile.HeightCm);
        Assert.Equal(62m, fx.Profile.WeightKg);
        Assert.Equal(Gender.Female, fx.Profile.Gender);
        Assert.Equal(FitnessGoal.BuildMuscle, fx.Profile.Goal);
        Assert.Equal(ActivityLevel.ModeratelyActive, fx.Profile.ActivityLevel);
        Assert.Equal(TrainingLocation.Home, fx.Profile.TrainingLocation);
        Assert.Equal(3, fx.Profile.TrainingDaysPerWeek);
        Assert.NotNull(fx.Profile.DateOfBirth);
        Assert.NotNull(fx.Profile.TargetWeightKg);

        var goal = Assert.Single(fx.Goals);
        Assert.True(goal.IsActive);
        Assert.Equal(FitnessGoal.BuildMuscle, goal.GoalType);
        Assert.Equal(62m, goal.StartingWeightKg);
        Assert.Equal(2100, Assert.Single(fx.NutritionGoals).DailyCalories);
        Assert.Equal(62m, Assert.Single(fx.Measurements).WeightKg);
        Assert.Equal(new long[] { 4, 9 }, fx.Equipment.Select(e => e.EquipmentId).OrderBy(id => id));
        var job = Assert.Single(fx.Jobs);
        Assert.Equal(PersonalizationJobStatus.Pending, job.Status);
        Assert.False(job.NutritionOnly);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_WhenAlreadyCompleted_ThrowsConflict_AndChangesNothing()
    {
        var fx = CreateFixture(heightCm: 180);

        await Assert.ThrowsAsync<ConflictException>(() => fx.Service.CompleteAsync(UserId, MakeRequest()));

        Assert.Equal(180m, fx.Profile.HeightCm);
        Assert.Empty(fx.Goals);
        Assert.Empty(fx.Jobs);
    }

    [Theory]
    [InlineData(true, false, nameof(FitnessProfileRequest.HeightCm))]
    [InlineData(false, true, nameof(FitnessProfileRequest.Weight))]
    public async Task CompleteAsync_RequiresHeightAndWeight(bool clearHeight, bool clearWeight, string field)
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        if (clearHeight) request.HeightCm = null;
        if (clearWeight) request.Weight = null;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.CompleteAsync(UserId, request));

        Assert.Contains(field, ex.Errors.Keys);
        Assert.Null(fx.Profile.HeightCm);
        fx.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_LoseWeightWithLowBodyFat_RequiresConfirmation()
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.Gender = "male";
        request.Age = 25;
        request.HeightCm = 180;
        request.Goal = "lose_weight";
        request.NeckCm = 40;
        request.WaistCm = 74;

        await Assert.ThrowsAsync<LowBodyFatConfirmationRequiredException>(() => fx.Service.CompleteAsync(UserId, request));
        Assert.Null(fx.Profile.HeightCm);

        request.ConfirmLowBodyFatWarning = true;
        await fx.Service.CompleteAsync(UserId, request);

        Assert.NotNull(Assert.Single(fx.Goals).LowBodyFatWarningAcknowledgedAt);
        Assert.Equal(40m, Assert.Single(fx.Measurements).NeckCm);
    }

    [Fact]
    public async Task CompleteAsync_UnknownUser_ThrowsNotFound()
    {
        var fx = CreateFixture();

        await Assert.ThrowsAsync<NotFoundException>(() => fx.Service.CompleteAsync(UserId + 1, MakeRequest()));
    }
}
