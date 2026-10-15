using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.AccountEmail;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Auth;

/// <summary>
/// Google ile giris/kayit: bagli hesapla giris, ayni dogrulanmis e-postali hesaba baglama, kayit gerektiginde
/// on doldurma bilgisi, parolasiz Google kaydi ve hesabi onceden ele gecirmeye karsi korumalar.
/// </summary>
public class AuthServiceGoogleTests
{
    private const string IdToken = "google-id-token";

    private sealed class Fixture
    {
        public InMemoryUnitOfWork Db { get; } = new();
        public FakeEmailSender Sender { get; } = new();
        public Mock<IGoogleIdTokenValidator> Google { get; } = new();
        public AuthService Service { get; }

        public Fixture()
        {
            Db.Register<ApplicationUser>().Register<UserProfile>().Register<UserEquipment>().Register<UserGoal>()
                .Register<BodyMeasurement>().Register<NutritionGoal>().Register<PersonalizationJob>()
                .Register<UserRefreshToken>().Register<UserAccountToken>().Register<ExternalLogin>();

            var tokenService = new Mock<ITokenService>();
            tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<ApplicationUser>())).Returns(("access", DateTime.UtcNow.AddHours(1)));
            tokenService.Setup(t => t.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));
            var calculator = new Mock<INutritionCalculator>();
            calculator.Setup(c => c.Calculate(It.IsAny<UserProfile>(), It.IsAny<MerfitCustomerApi.Business.Services.BodyComposition.BodyFatAssessment?>())).Returns(new NutritionCalculationResult { DailyCalories = 2000 });

            var accountEmails = new AccountEmailService(Db.Object, Sender, Options.Create(new AccountEmailOptions()), TimeProvider.System, NullLogger<AccountEmailService>.Instance);
            Service = new AuthService(Db.Object, tokenService.Object, calculator.Object, Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 }), accountEmails, Google.Object);
            GoogleReturns(new GoogleIdentity("google-sub-1", "mert@gmail.com", EmailVerified: true, "Mert", "Akpınar"));
        }

        public void GoogleReturns(GoogleIdentity identity) =>
            Google.Setup(g => g.ValidateAsync(IdToken, It.IsAny<CancellationToken>())).ReturnsAsync(identity);

        public Task<AuthResponse> RegisterWithPasswordAsync(string email = "mert@gmail.com") =>
            Service.RegisterAsync(new RegisterRequest
            {
                FirstName = "Mert", LastName = "Akpınar", Username = "mert", Email = email,
                Password = "password1", ConfirmPassword = "password1", HeightUnit = "cm", HeightCm = 180, WeightUnit = "kg", Weight = 78,
            }, null);
    }

    private static GoogleSignInRequest SignIn => new() { IdToken = IdToken };

    private static GoogleRegisterRequest Register(string username = "mert_g") => new()
    {
        IdToken = IdToken, FirstName = "Mert", LastName = "Akpınar", Username = username,
        HeightUnit = "cm", HeightCm = 180, WeightUnit = "kg", Weight = 78, Goal = "build_muscle",
    };

    [Fact]
    public async Task SignIn_WithoutAccount_RequiresRegistrationAndReturnsGoogleProfile()
    {
        var fx = new Fixture();

        var result = await fx.Service.GoogleSignInAsync(SignIn, null);

        Assert.True(result.RegistrationRequired);
        Assert.Null(result.Auth);
        Assert.Equal("mert@gmail.com", result.Profile!.Email);
        Assert.Equal("Mert", result.Profile.FirstName);
        Assert.Equal("Akpınar", result.Profile.LastName);
        Assert.Empty(fx.Db.Set<ApplicationUser>());
        Assert.Empty(fx.Db.Set<ExternalLogin>());
    }

    [Fact]
    public async Task Register_CreatesVerifiedPasswordlessAccountLinkedToGoogle()
    {
        var fx = new Fixture();

        var result = await fx.Service.GoogleRegisterAsync(Register(), "1.2.3.4");

        Assert.True(result.IsNewUser);
        Assert.True(result.EmailConfirmed);
        Assert.Null(result.VerificationEmailSent);
        Assert.Empty(fx.Sender.Sent);
        Assert.Equal("mert_g", result.Username);
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));

        var user = Assert.Single(fx.Db.Set<ApplicationUser>());
        Assert.Equal("mert@gmail.com", user.Email);
        Assert.Equal("MERT@GMAIL.COM", user.NormalizedEmail);
        Assert.True(user.EmailConfirmed);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        var link = Assert.Single(fx.Db.Set<ExternalLogin>());
        Assert.Equal((user.Id, "Google", "google-sub-1"), (link.UserId, link.Provider, link.ProviderUserId));
        Assert.Single(fx.Db.Set<UserProfile>());
        Assert.Single(fx.Db.Set<PersonalizationJob>());

        // Parolasiz hesap: hicbir parola ile giris yapilamaz, ama Google ile giris yapilir.
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert@gmail.com", Password = "password1" }, null));
        var signIn = await fx.Service.GoogleSignInAsync(SignIn, null);
        Assert.False(signIn.RegistrationRequired);
        Assert.Equal(user.Id, signIn.Auth!.UserId);
        Assert.False(signIn.Auth.IsNewUser);
        Assert.Equal("mert_g", signIn.Auth.Username);
    }

    [Fact]
    public async Task Register_Twice_SignsInInsteadOfCreatingSecondAccount()
    {
        var fx = new Fixture();
        await fx.Service.GoogleRegisterAsync(Register(), null);

        var again = await fx.Service.GoogleRegisterAsync(Register(username: "baska_ad"), null);

        Assert.False(again.IsNewUser);
        Assert.Equal("mert_g", again.Username);
        Assert.Single(fx.Db.Set<ApplicationUser>());
        Assert.Single(fx.Db.Set<ExternalLogin>());
    }

    [Fact]
    public async Task Register_WithTakenUsername_IsRejected()
    {
        var fx = new Fixture();
        await fx.RegisterWithPasswordAsync(email: "other@example.com");

        await Assert.ThrowsAsync<ConflictException>(() => fx.Service.GoogleRegisterAsync(Register(username: "mert"), null));
        Assert.Empty(fx.Db.Set<ExternalLogin>());
    }

    [Fact]
    public async Task SignIn_ExistingVerifiedPasswordAccount_LinksAndKeepsPassword()
    {
        var fx = new Fixture();
        await fx.RegisterWithPasswordAsync();
        var user = fx.Db.Set<ApplicationUser>().Single();
        user.EmailConfirmed = true;
        var passwordHash = user.PasswordHash;

        var result = await fx.Service.GoogleSignInAsync(SignIn, null);

        Assert.False(result.RegistrationRequired);
        Assert.Equal(user.Id, result.Auth!.UserId);
        Assert.Equal("mert", result.Auth.Username);
        Assert.Equal(passwordHash, user.PasswordHash);
        Assert.Single(fx.Db.Set<ExternalLogin>());
        // Parolayla giris de calismaya devam eder.
        await fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert", Password = "password1" }, null);
    }

    [Fact]
    public async Task SignIn_EmailMatchIsCaseInsensitive()
    {
        var fx = new Fixture();
        await fx.RegisterWithPasswordAsync(email: "Mert@Gmail.com");
        fx.Db.Set<ApplicationUser>().Single().EmailConfirmed = true;

        var result = await fx.Service.GoogleSignInAsync(SignIn, null);

        Assert.False(result.RegistrationRequired);
    }

    [Fact]
    public async Task SignIn_ExistingUnverifiedAccount_IsTakenOverByGoogleOwner()
    {
        // Baskasi bu e-postayla parolali hesap acip dogrulamadan birakmis olabilir: Google adresin sahibini
        // kanitladigi icin hesap dogrulanir, eski parola ve oturumlar gecersiz olur.
        var fx = new Fixture();
        await fx.RegisterWithPasswordAsync();
        var user = fx.Db.Set<ApplicationUser>().Single();
        Assert.False(user.EmailConfirmed);

        var result = await fx.Service.GoogleSignInAsync(SignIn, "5.6.7.8");

        Assert.True(result.Auth!.EmailConfirmed);
        Assert.True(user.EmailConfirmed);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fx.Service.LoginAsync(new LoginRequest { EmailOrUsername = "mert", Password = "password1" }, null));
        var tokens = fx.Db.Set<UserRefreshToken>().ToList();
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens.Single(t => t.Token != result.Auth.RefreshToken).IsRevoked);
        Assert.False(tokens.Single(t => t.Token == result.Auth.RefreshToken).IsRevoked);
    }

    [Fact]
    public async Task SignIn_GoogleEmailNotVerified_DoesNotLinkExistingAccount()
    {
        var fx = new Fixture();
        await fx.RegisterWithPasswordAsync();
        fx.GoogleReturns(new GoogleIdentity("google-sub-1", "mert@gmail.com", EmailVerified: false, "Mert", null));

        await Assert.ThrowsAsync<ConflictException>(() => fx.Service.GoogleSignInAsync(SignIn, null));
        await Assert.ThrowsAsync<ConflictException>(() => fx.Service.GoogleRegisterAsync(Register(), null));
        Assert.Empty(fx.Db.Set<ExternalLogin>());
    }

    [Fact]
    public async Task Register_GoogleEmailNotVerified_CreatesAccountThatNeedsEmailCode()
    {
        var fx = new Fixture();
        fx.GoogleReturns(new GoogleIdentity("google-sub-2", "ali@ornek.com", EmailVerified: false, "Ali", null));

        var result = await fx.Service.GoogleRegisterAsync(Register(), null);

        Assert.False(result.EmailConfirmed);
        Assert.True(result.VerificationEmailSent);
        Assert.Equal("ali@ornek.com", Assert.Single(fx.Sender.Sent).Email.To);
    }

    [Fact]
    public async Task SignIn_LinkedByGoogleId_EvenIfGoogleEmailChanged()
    {
        var fx = new Fixture();
        await fx.Service.GoogleRegisterAsync(Register(), null);
        fx.GoogleReturns(new GoogleIdentity("google-sub-1", "yeni-adres@gmail.com", EmailVerified: true, "Mert", "Akpınar"));

        var result = await fx.Service.GoogleSignInAsync(SignIn, null);

        Assert.Equal("mert@gmail.com", result.Auth!.Email);
        Assert.Single(fx.Db.Set<ApplicationUser>());
    }

    [Fact]
    public async Task SignIn_InactiveAccount_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.GoogleRegisterAsync(Register(), null);
        fx.Db.Set<ApplicationUser>().Single().IsActive = false;

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.Service.GoogleSignInAsync(SignIn, null));
    }

    [Fact]
    public async Task InvalidToken_IsRejectedBeforeAnyLookup()
    {
        var fx = new Fixture();
        fx.Google.Setup(g => g.ValidateAsync(IdToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("Google ile giriş doğrulanamadı. Lütfen tekrar dene."));

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.Service.GoogleSignInAsync(SignIn, null));
        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.Service.GoogleRegisterAsync(Register(), null));
        Assert.Empty(fx.Db.Set<ApplicationUser>());
    }

    [Fact]
    public async Task Validator_WithoutClientIds_Returns503StyleError()
    {
        var validator = new GoogleIdTokenValidator(Options.Create(new GoogleAuthOptions()), NullLogger<GoogleIdTokenValidator>.Instance);

        Assert.False(validator.IsConfigured);
        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => validator.ValidateAsync("x"));
    }

    [Fact]
    public async Task Validator_RejectsMalformedToken_AsUnauthorized()
    {
        var validator = new GoogleIdTokenValidator(
            Options.Create(new GoogleAuthOptions { ClientIds = { "web-client.apps.googleusercontent.com" } }),
            NullLogger<GoogleIdTokenValidator>.Instance);

        Assert.True(validator.IsConfigured);
        await Assert.ThrowsAsync<UnauthorizedException>(() => validator.ValidateAsync("not-a-jwt"));
    }
}
