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
        nutritionCalculator.Setup(n => n.Calculate(It.IsAny<UserProfile>()))
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
            Service = new AuthService(unitOfWork.Object, tokenService.Object, nutritionCalculator.Object, jwtSettings),
            Users = users,
            Profiles = profiles,
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
}
