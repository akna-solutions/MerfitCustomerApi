using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>
/// Register akisinda FirstName/LastName'in artik tek parcali "Name"den heuristik olarak degil,
/// dogrudan ayri alanlardan UserProfile'a yazildigini ve Username benzersizlik kontrolunun
/// (bkz. AuthService.RegisterAsync) calistigini dogrular.
/// </summary>
public class AuthServiceRegistrationTests
{
    private sealed class Fixture
    {
        public required AuthService Service { get; init; }
        public required List<ApplicationUser> Users { get; init; }
        public required List<UserProfile> Profiles { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
        public required List<UserGoal> Goals { get; init; }
        public required List<BodyMeasurement> Measurements { get; init; }
        public required List<NutritionGoal> NutritionGoals { get; init; }
        public required List<UserEquipment> Equipment { get; init; }
    }

    private static readonly Mock<IAccountEmailService> AccountEmails = CreateAccountEmails();

    private static Mock<IAccountEmailService> CreateAccountEmails()
    {
        var mock = new Mock<IAccountEmailService>();
        mock.Setup(m => m.TrySendVerificationAfterRegistrationAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Fixture CreateFixture()
    {
        var users = new List<ApplicationUser>();
        var profiles = new List<UserProfile>();
        var equipment = new List<UserEquipment>();
        var goals = new List<UserGoal>();
        var nutritionGoals = new List<NutritionGoal>();
        var jobs = new List<PersonalizationJob>();
        var refreshTokens = new List<UserRefreshToken>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<ApplicationUser>()).Returns(MockRepositoryFactory.Create(users).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(profiles).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(equipment).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(goals).Object);
        var measurements = new List<BodyMeasurement>();
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(measurements).Object);
        unitOfWork.Setup(u => u.Repository<NutritionGoal>()).Returns(MockRepositoryFactory.Create(nutritionGoals).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.Repository<UserRefreshToken>()).Returns(MockRepositoryFactory.Create(refreshTokens).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<ApplicationUser>()))
            .Returns(("access-token", DateTime.UtcNow.AddHours(1)));
        tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");

        var nutritionCalculator = new Mock<INutritionCalculator>();
        nutritionCalculator.Setup(n => n.Calculate(It.IsAny<UserProfile>(), It.IsAny<MerfitCustomerApi.Business.Services.BodyComposition.BodyFatAssessment?>()))
            .Returns(new NutritionCalculationResult
            {
                DailyCalories = 2000,
                ProteinTarget = 150,
                CarbsTarget = 200,
                FatTarget = 60,
                WaterTargetMl = 2500,
            });

        var jwtSettings = Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 });

        return new Fixture
        {
            Service = new AuthService(unitOfWork.Object, tokenService.Object, nutritionCalculator.Object, jwtSettings, AccountEmails.Object, Mock.Of<IGoogleIdTokenValidator>()),
            Users = users,
            Profiles = profiles,
            Jobs = jobs,
            Goals = goals,
            Measurements = measurements,
            NutritionGoals = nutritionGoals,
            Equipment = equipment,
        };
    }

    private static RegisterRequest MakeRequest(
        string firstName = "Mert",
        string lastName = "Akpınar",
        string username = "mert",
        string email = "mert@example.com") => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Username = username,
        Email = email,
        Password = "password1",
        ConfirmPassword = "password1",
        HeightUnit = "cm",
        HeightCm = 180,
        WeightUnit = "kg",
        Weight = 78,
    };

    [Fact]
    public async Task RegisterAsync_SavesFirstNameAndLastNameSeparately()
    {
        var fx = CreateFixture();

        await fx.Service.RegisterAsync(MakeRequest(firstName: "Mert", lastName: "Akpınar"), ipAddress: null);

        var profile = Assert.Single(fx.Profiles);
        Assert.Equal("Mert", profile.FirstName);
        Assert.Equal("Akpınar", profile.LastName);
    }

    /// <summary>
    /// Kayit sirasinda plan uretilmez; yalnizca Pending bir PersonalizationJob olusturulur ve program
    /// arka planda (PersonalizationJobProcessor) uretilir. Antrenman puanlama sistemi bu akisi degistirmez.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_CreatesPendingPersonalizationJob_WithoutGeneratingPlanSynchronously()
    {
        var fx = CreateFixture();

        var result = await fx.Service.RegisterAsync(MakeRequest(), ipAddress: null);

        var job = Assert.Single(fx.Jobs);
        Assert.Equal(PersonalizationJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(result.UserId, job.UserId);
    }

    [Theory]
    [InlineData("lose_weight", 95, 71.5)]
    [InlineData("build_muscle", 70, 74)]
    [InlineData("maintain_weight", 78, 82.5)] // eski hedef kodu -> Kas Gelistirme
    public async Task RegisterAsync_SetsTargetWeightFromGoal_AndFirstWeightEntry(string goal, double weight, double expectedTarget)
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.Goal = goal;
        request.Weight = (decimal)weight;

        await fx.Service.RegisterAsync(request, ipAddress: null);

        Assert.Equal((decimal)expectedTarget, Assert.Single(fx.Profiles).TargetWeightKg);
        Assert.Equal((decimal)expectedTarget, Assert.Single(fx.Goals).TargetWeightKg);
        Assert.Equal((decimal)weight, Assert.Single(fx.Measurements).WeightKg);
    }

    [Fact]
    public async Task RegisterAsync_LoseWeightWithLowBodyFat_RequiresConfirmation_AndCreatesNothing()
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.Goal = "lose_weight";
        request.Gender = "male";
        request.Age = 25;
        request.NeckCm = 40;
        request.WaistCm = 74; // US Navy ~%4.6, erkek 18-39 icin dusuk (<8)

        await Assert.ThrowsAsync<LowBodyFatConfirmationRequiredException>(() => fx.Service.RegisterAsync(request, ipAddress: null));

        Assert.Empty(fx.Users);
        Assert.Empty(fx.Profiles);
        Assert.Empty(fx.Jobs);
    }

    [Fact]
    public async Task RegisterAsync_LoseWeightWithLowBodyFat_AndConfirmation_SavesMeasurementAndAcknowledgement()
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.Goal = "lose_weight";
        request.Gender = "male";
        request.Age = 25;
        request.NeckCm = 40;
        request.WaistCm = 74;
        request.ConfirmLowBodyFatWarning = true;

        await fx.Service.RegisterAsync(request, ipAddress: null);

        Assert.Equal(FitnessGoal.LoseWeight, Assert.Single(fx.Profiles).Goal);
        Assert.NotNull(Assert.Single(fx.Goals).LowBodyFatWarningAcknowledgedAt);
        var measurement = Assert.Single(fx.Measurements);
        Assert.Equal(40m, measurement.NeckCm);
        Assert.NotNull(measurement.EstimatedBodyFatPercentage);
        Assert.Null(measurement.BodyFatPercentage);
    }

    [Fact]
    public async Task RegisterAsync_LoseWeightWithoutMeasurements_ShowsNoWarning()
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.Goal = "lose_weight";
        request.Gender = "male";
        request.Age = 25;

        await fx.Service.RegisterAsync(request, ipAddress: null);

        Assert.Null(Assert.Single(fx.Goals).LowBodyFatWarningAcknowledgedAt);
    }

    [Fact]
    public async Task RegisterAsync_SavesChosenUsername()
    {
        var fx = CreateFixture();

        var result = await fx.Service.RegisterAsync(MakeRequest(username: "mertakpinar"), ipAddress: null);

        Assert.Equal("mertakpinar", Assert.Single(fx.Profiles).Username);
        Assert.Equal("mertakpinar", result.Username);
    }

    [Fact]
    public async Task RegisterAsync_ThrowsConflict_WhenUsernameAlreadyTaken()
    {
        var fx = CreateFixture();
        fx.Users.Add(new ApplicationUser
        {
            Id = 1,
            Email = "existing@example.com",
            NormalizedEmail = "EXISTING@EXAMPLE.COM",
            UserName = "existing@example.com",
            NormalizedUserName = "EXISTING@EXAMPLE.COM",
            PasswordHash = "hash",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        fx.Profiles.Add(new UserProfile
        {
            Id = 1,
            UserId = 1,
            FirstName = "Ahmet",
            LastName = "Yılmaz",
            Username = "mert",
            UnitSystem = UnitSystem.Metric,
            CreatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<ConflictException>(
            () => fx.Service.RegisterAsync(MakeRequest(username: "mert", email: "new@example.com"), ipAddress: null));

        Assert.Single(fx.Profiles); // yeni bir profil eklenmedi
    }

    [Fact]
    public async Task RegisterAsync_ThrowsConflict_WhenEmailAlreadyTaken()
    {
        var fx = CreateFixture();
        fx.Users.Add(new ApplicationUser
        {
            Id = 1,
            Email = "mert@example.com",
            NormalizedEmail = "MERT@EXAMPLE.COM",
            UserName = "mert@example.com",
            NormalizedUserName = "MERT@EXAMPLE.COM",
            PasswordHash = "hash",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<ConflictException>(
            () => fx.Service.RegisterAsync(MakeRequest(email: "mert@example.com", username: "farkliuser"), ipAddress: null));

        Assert.Empty(fx.Profiles);
    }

    [Fact]
    public async Task RegisterAsync_WithoutHeight_StillRequiresHeight_ForFitnessRegistration()
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.HeightCm = null;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.RegisterAsync(request, ipAddress: null));

        Assert.Contains(nameof(RegisterRequest.HeightCm), ex.Errors.Keys);
        Assert.Empty(fx.Users);
    }

    [Fact]
    public async Task RegisterAsync_FitnessRegistration_ReportsCompletedFitnessProfile()
    {
        var fx = CreateFixture();

        var result = await fx.Service.RegisterAsync(MakeRequest(), ipAddress: null);

        Assert.True(result.FitnessProfileCompleted);
        Assert.Equal("Pending", result.PersonalizationStatus);
        Assert.Single(fx.NutritionGoals);
    }

    /// <summary>
    /// Yalnizca alisveris icin kayit: boy/kilo/hedef sorulmaz (gonderilse de yok sayilir) ve kisisellestirme
    /// kayitlari (hedef, beslenme hedefi, ekipman, olcum, plan isi) olusturulmaz.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ShoppingOnly_CreatesAccountWithoutFitnessData()
    {
        var fx = CreateFixture();
        var request = new RegisterRequest
        {
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            Username = "ayse",
            Email = "ayse@example.com",
            Password = "password1",
            ConfirmPassword = "password1",
            ShoppingOnly = true,
            // Eski/hatali istemci fitness alanlari gonderse de yazilmaz.
            Goal = "lose_weight",
            Weight = 60,
            EquipmentIds = new List<long> { 1, 2 },
        };

        var result = await fx.Service.RegisterAsync(request, ipAddress: null);

        var profile = Assert.Single(fx.Profiles);
        Assert.Equal("Ayşe", profile.FirstName);
        Assert.Equal("ayse", profile.Username);
        Assert.Null(profile.HeightCm);
        Assert.Null(profile.WeightKg);
        Assert.Null(profile.Goal);
        Assert.Equal(UnitSystem.Metric, profile.UnitSystem);
        Assert.Empty(fx.Goals);
        Assert.Empty(fx.NutritionGoals);
        Assert.Empty(fx.Jobs);
        Assert.Empty(fx.Measurements);
        Assert.Empty(fx.Equipment);

        Assert.False(result.FitnessProfileCompleted);
        Assert.Null(result.PersonalizationStatus);
        Assert.True(result.IsNewUser);
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LoginAsync_ReportsWhetherFitnessProfileIsCompleted(bool shoppingOnly, bool expectedCompleted)
    {
        var fx = CreateFixture();
        var request = MakeRequest();
        request.ShoppingOnly = shoppingOnly;
        await fx.Service.RegisterAsync(request, ipAddress: null);

        var login = await fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert", Password = "password1" }, null);

        Assert.Equal(expectedCompleted, login.FitnessProfileCompleted);
    }
}
