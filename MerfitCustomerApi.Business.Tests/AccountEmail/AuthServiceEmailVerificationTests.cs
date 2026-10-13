using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// E-posta dogrulamasi mevcut kayit ve giris akislarini bozmaz: dogrulama e-postasi gonderilemese de kayit
/// tamamlanir; dogrulanmamis (mevcut) hesaplar giris yapmaya devam eder.
/// </summary>
public class AuthServiceEmailVerificationTests
{
    private sealed class Fixture
    {
        public InMemoryUnitOfWork Db { get; } = new();
        public FakeEmailSender Sender { get; } = new();
        public AuthService Service { get; }

        public Fixture()
        {
            Db.Register<ApplicationUser>().Register<UserProfile>().Register<UserEquipment>().Register<UserGoal>()
                .Register<BodyMeasurement>().Register<NutritionGoal>().Register<PersonalizationJob>()
                .Register<UserRefreshToken>().Register<UserAccountToken>();

            var tokenService = new Mock<ITokenService>();
            tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<ApplicationUser>())).Returns(("access", DateTime.UtcNow.AddHours(1)));
            tokenService.Setup(t => t.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));
            var calculator = new Mock<INutritionCalculator>();
            calculator.Setup(c => c.Calculate(It.IsAny<UserProfile>())).Returns(new NutritionCalculationResult { DailyCalories = 2000 });

            var accountEmails = new AccountEmailService(Db.Object, Sender, Options.Create(new AccountEmailOptions()), TimeProvider.System, NullLogger<AccountEmailService>.Instance);
            Service = new AuthService(Db.Object, tokenService.Object, calculator.Object, Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 }), accountEmails);
        }
    }

    private static RegisterRequest Request => new()
    {
        FirstName = "Mert", LastName = "Akpınar", Username = "mert", Email = "mert@example.com",
        Password = "password1", ConfirmPassword = "password1", HeightUnit = "cm", HeightCm = 180, WeightUnit = "kg", Weight = 78,
    };

    [Fact]
    public async Task Register_SendsVerificationEmail_AndReturnsUnverifiedSession()
    {
        var fx = new Fixture();

        var result = await fx.Service.RegisterAsync(Request, "1.2.3.4");

        Assert.False(result.EmailConfirmed);
        Assert.True(result.VerificationEmailSent);
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        var (email, _) = Assert.Single(fx.Sender.Sent);
        Assert.Equal("mert@example.com", email.To);
        Assert.Contains("Merhaba Mert,", email.TextBody);
        Assert.Matches("^[0-9]{6}$", FakeEmailSender.ExtractCode(email));
    }

    [Fact]
    public async Task Register_WhenEmailCannotBeSent_StillCreatesAccount()
    {
        var fx = new Fixture();
        fx.Sender.FailWith = new EmailDeliveryException(ResendEmailSender.UnavailableMessage, isConfigurationError: false);

        var result = await fx.Service.RegisterAsync(Request, null);

        Assert.False(result.VerificationEmailSent);
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.Single(fx.Db.Set<ApplicationUser>());
        Assert.Single(fx.Db.Set<UserProfile>());
    }

    [Fact]
    public async Task Register_WhenApiKeyMissing_StillCreatesAccount()
    {
        var fx = new Fixture();
        fx.Sender.IsConfigured = false;

        var result = await fx.Service.RegisterAsync(Request, null);

        Assert.False(result.VerificationEmailSent);
        Assert.Single(fx.Db.Set<ApplicationUser>());
    }

    [Fact]
    public async Task Login_UnverifiedExistingUser_CanStillSignIn()
    {
        var fx = new Fixture();
        await fx.Service.RegisterAsync(Request, null);

        var byEmail = await fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert@example.com", Password = "password1" }, null);
        var byUsername = await fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert", Password = "password1" }, null);

        Assert.False(byEmail.EmailConfirmed);
        Assert.Null(byEmail.VerificationEmailSent);
        Assert.False(string.IsNullOrEmpty(byUsername.AccessToken));
    }

    [Fact]
    public async Task Login_AfterVerification_ReportsConfirmedEmail()
    {
        var fx = new Fixture();
        await fx.Service.RegisterAsync(Request, null);
        fx.Db.Set<ApplicationUser>().Single().EmailConfirmed = true;

        var result = await fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert@example.com", Password = "password1" }, null);

        Assert.True(result.EmailConfirmed);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsStillRejected()
    {
        var fx = new Fixture();
        await fx.Service.RegisterAsync(Request, null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert@example.com", Password = "wrong-pass" }, null));
    }

    // --- Kullanici adi anlik kontrolu ---

    [Theory]
    [InlineData("mert", false, "Bu kullanıcı adı zaten kullanılıyor.")]
    [InlineData("  MERT ", false, "Bu kullanıcı adı zaten kullanılıyor.")]
    [InlineData("mert_2", true, null)]
    [InlineData("ab", false, "Kullanıcı adı 3-30 karakter arasında olmalıdır.")]
    [InlineData("", false, "Kullanıcı adı 3-30 karakter arasında olmalıdır.")]
    [InlineData(null, false, "Kullanıcı adı 3-30 karakter arasında olmalıdır.")]
    [InlineData("mert akpınar", false, "Kullanıcı adı yalnızca harf, rakam ve alt çizgi (_) içerebilir.")]
    [InlineData("şule", false, "Kullanıcı adı yalnızca harf, rakam ve alt çizgi (_) içerebilir.")]
    public async Task UsernameAvailability_ChecksFormatAndExistingProfiles(string? username, bool available, string? message)
    {
        var fx = new Fixture();
        await fx.Service.RegisterAsync(Request, null);

        var result = await fx.Service.CheckUsernameAvailabilityAsync(username);

        Assert.Equal(available, result.Available);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task UsernameAvailability_AgreesWithRegistration()
    {
        var fx = new Fixture();
        Assert.True((await fx.Service.CheckUsernameAvailabilityAsync("mert")).Available);

        await fx.Service.RegisterAsync(Request, null);

        Assert.False((await fx.Service.CheckUsernameAvailabilityAsync("mert")).Available);
        var duplicate = Request;
        duplicate.Email = "other@example.com";
        await Assert.ThrowsAsync<ConflictException>(() => fx.Service.RegisterAsync(duplicate, null));
    }
}
