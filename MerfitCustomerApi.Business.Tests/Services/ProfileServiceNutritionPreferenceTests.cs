using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>
/// PUT /api/profile: beslenme tercihleri (diyet, alerji, dislanan besin, aktivite) dogrulanarak saklanir
/// ve beslenmeyi etkileyen her profil degisikligi programin yeniden uretilmesini tetikler. Yeni alanlari
/// gondermeyen eski istemciler hicbir tercihi degistirmez.
/// </summary>
public class ProfileServiceNutritionPreferenceTests
{
    private const long UserId = 7;

    private sealed class Fixture
    {
        public required ProfileService Service { get; init; }
        public required UserProfile Profile { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
    }

    private static Fixture CreateFixture(params PersonalizationJob[] existingJobs)
    {
        var profile = new UserProfile
        {
            Id = 1,
            UserId = UserId,
            FirstName = "Ayse",
            LastName = "Kaya",
            Username = "ayse",
            WeightKg = 70,
            HeightCm = 168,
            DateOfBirth = new DateTime(1994, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Female,
            Goal = FitnessGoal.LoseWeight,
            ExperienceLevel = ExperienceLevel.Beginner,
            ActivityLevel = ActivityLevel.LightlyActive,
            TrainingDaysPerWeek = 3,
            TrainingLocation = TrainingLocation.Gym,
        };
        var jobs = existingJobs.ToList();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(new List<ApplicationUser> { new() { Id = UserId, Email = "ayse@example.com" } }).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(new List<UserGoal>()).Object);
        unitOfWork.Setup(u => u.Repository<GoalHistory>()).Returns(MockRepositoryFactory.Create(new List<GoalHistory>()).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(new List<BodyMeasurement>()).Object);
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(new List<UserPreference>()).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(new List<UserNotificationSetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserPrivacySetting>()).Returns(MockRepositoryFactory.Create(new List<UserPrivacySetting>()).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(new List<UserEquipment>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(new List<WorkoutSession>()).Object);
        unitOfWork.Setup(u => u.Repository<UserStreak>()).Returns(MockRepositoryFactory.Create(new List<UserStreak>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlan>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(new List<WorkoutPlanDay>()).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.Repository<Food>()).Returns(MockRepositoryFactory.Create(NutritionTestCatalog.Create()).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new Fixture { Service = new ProfileService(unitOfWork.Object), Profile = profile, Jobs = jobs };
    }

    [Theory]
    [InlineData("weight")]
    [InlineData("height")]
    [InlineData("targetWeight")]
    [InlineData("dateOfBirth")]
    [InlineData("activity")]
    public async Task NutritionRelevantChange_EnqueuesNutritionOnlyRegeneration(string field)
    {
        var fixture = CreateFixture();
        var request = field switch
        {
            "weight" => new UpdateProfileRequest { Weight = 66 },
            "height" => new UpdateProfileRequest { Height = 170 },
            "targetWeight" => new UpdateProfileRequest { TargetWeight = 62 },
            "dateOfBirth" => new UpdateProfileRequest { DateOfBirth = "1990-01-01" },
            _ => new UpdateProfileRequest { ActivityLevel = "active" },
        };

        await fixture.Service.UpdateProfileAsync(UserId, request);

        var job = Assert.Single(fixture.Jobs);
        Assert.Equal(PersonalizationJobStatus.Pending, job.Status);
        Assert.True(job.NutritionOnly);
    }

    [Fact]
    public async Task GoalChange_EnqueuesFullRegeneration()
    {
        var fixture = CreateFixture();

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Goal = "BUILD_MUSCLE", Weight = 66 });

        Assert.False(Assert.Single(fixture.Jobs).NutritionOnly);
    }

    [Fact]
    public async Task PendingNutritionOnlyJob_IsWidenedToFull_InsteadOfDuplicated()
    {
        var fixture = CreateFixture(new PersonalizationJob { Id = 3, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true });

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { TrainingDaysPerWeek = 5 });

        var job = Assert.Single(fixture.Jobs);
        Assert.False(job.NutritionOnly);
    }

    [Fact]
    public async Task ProcessingJob_DoesNotSwallowNewerChange()
    {
        // Islenmekte olan is eski profili okumus olabilir; yeni degisiklik icin ayrica bir is kuyruga alinir.
        var fixture = CreateFixture(new PersonalizationJob { Id = 3, UserId = UserId, Status = PersonalizationJobStatus.Processing, StartedAt = DateTime.UtcNow });

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Weight = 65 });

        Assert.Equal(2, fixture.Jobs.Count);
        Assert.Contains(fixture.Jobs, j => j.Status == PersonalizationJobStatus.Pending && j.NutritionOnly);
    }

    [Fact]
    public async Task Preferences_AreValidated_Stored_AndReturned()
    {
        var fixture = CreateFixture();

        var response = await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest
        {
            DietaryPreference = "vegetarian",
            Allergies = new List<string> { "gluten", "treeNuts" },
            ExcludedFoodIds = new List<long> { 22, 9, 22 },
        });

        Assert.Equal(DietaryPreference.Vegetarian, fixture.Profile.DietaryPreference);
        Assert.Equal(FoodAllergen.Gluten | FoodAllergen.TreeNuts, fixture.Profile.Allergies);
        Assert.Equal("9,22", fixture.Profile.ExcludedFoodIds);
        Assert.Equal("vegetarian", response.DietaryPreference);
        Assert.Equal(new[] { "gluten", "treeNuts" }, response.Allergies);
        Assert.Equal(new long[] { 9, 22 }, response.ExcludedFoodIds);
        Assert.True(Assert.Single(fixture.Jobs).NutritionOnly);
    }

    [Fact]
    public async Task EmptyAllergyList_ClearsAllergies()
    {
        var fixture = CreateFixture();
        fixture.Profile.Allergies = FoodAllergen.Milk;

        await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { Allergies = new List<string>() });

        Assert.Equal(FoodAllergen.None, fixture.Profile.Allergies);
    }

    [Theory]
    [InlineData("allergy")]
    [InlineData("diet")]
    [InlineData("food")]
    [InlineData("activity")]
    public async Task InvalidPreference_IsRejected_AndNothingIsChanged(string field)
    {
        var fixture = CreateFixture();
        var request = field switch
        {
            "allergy" => new UpdateProfileRequest { Allergies = new List<string> { "gluten", "kiwi" } },
            "diet" => new UpdateProfileRequest { DietaryPreference = "carnivore" },
            "food" => new UpdateProfileRequest { ExcludedFoodIds = new List<long> { 1, 123456 } },
            _ => new UpdateProfileRequest { ActivityLevel = "2" },
        };

        await Assert.ThrowsAsync<AppValidationException>(() => fixture.Service.UpdateProfileAsync(UserId, request));

        Assert.Null(fixture.Profile.Allergies);
        Assert.Null(fixture.Profile.DietaryPreference);
        Assert.Null(fixture.Profile.ExcludedFoodIds);
        Assert.Equal(ActivityLevel.LightlyActive, fixture.Profile.ActivityLevel);
        Assert.Empty(fixture.Jobs);
    }

    [Fact]
    public async Task LegacyClientRequest_WithoutNewFields_ChangesNoPreference_AndTriggersNothing()
    {
        var fixture = CreateFixture();
        fixture.Profile.DietaryPreference = DietaryPreference.Vegan;
        fixture.Profile.Allergies = FoodAllergen.Soy;
        fixture.Profile.ExcludedFoodIds = "4";

        var response = await fixture.Service.UpdateProfileAsync(UserId, new UpdateProfileRequest { FirstName = "Ayşe" });

        Assert.Equal(DietaryPreference.Vegan, fixture.Profile.DietaryPreference);
        Assert.Equal(FoodAllergen.Soy, fixture.Profile.Allergies);
        Assert.Equal("4", fixture.Profile.ExcludedFoodIds);
        Assert.Empty(fixture.Jobs);
        // Mevcut alanlar ayni sekilde doner.
        Assert.Equal(70, response.Weight);
        Assert.Equal("light", response.ActivityLevel);
        Assert.Equal(3, response.TrainingDaysPerWeek);
    }
}
