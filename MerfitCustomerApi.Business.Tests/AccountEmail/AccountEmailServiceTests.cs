using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama is kurallari: tek kullanimlik/sureli jeton, yalnizca ozetin saklanmasi,
/// hesap kesfini onleyen genel yanit, bekleme suresi ve saatlik sinir, sifre degisince oturumlarin kapanmasi.
/// </summary>
public class AccountEmailServiceTests
{
    private const string UserEmail = "Mert@Example.com";

    private sealed class Fixture
    {
        public InMemoryUnitOfWork Db { get; } = new();
        public FakeEmailSender Sender { get; } = new();
        public MutableTimeProvider Clock { get; } = new();
        public AccountEmailOptions Options { get; } = new();
        public ApplicationUser User { get; }
        public AccountEmailService Service { get; }

        public Fixture(bool emailConfirmed = false)
        {
            Db.Register<UserAccountToken>().Register<UserRefreshToken>();
            User = new ApplicationUser
            {
                Id = 7,
                Email = UserEmail,
                NormalizedEmail = UserEmail.ToUpperInvariant(),
                UserName = UserEmail,
                NormalizedUserName = UserEmail.ToUpperInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("old-password"),
                EmailConfirmed = emailConfirmed,
                IsActive = true,
                CreatedAt = Clock.Now.UtcDateTime,
            };
            Db.Set<ApplicationUser>().Add(User);
            Db.Set<UserProfile>().Add(new UserProfile { Id = 1, UserId = User.Id, FirstName = "Mert", LastName = "A", Username = "mert" });
            Service = new AccountEmailService(Db.Object, Sender, Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<AccountEmailService>.Instance);
        }

        public List<UserAccountToken> Tokens => Db.Set<UserAccountToken>();

        public string LastToken => FakeEmailSender.ExtractToken(Sender.Sent[^1].Email);
    }

    // --- E-posta dogrulama ---

    [Fact]
    public async Task Registration_SendsVerificationEmail_AndStoresOnlyTokenHash()
    {
        var fx = new Fixture();

        var sent = await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", "1.2.3.4");

        Assert.True(sent);
        var (email, key) = Assert.Single(fx.Sender.Sent);
        Assert.Equal(UserEmail, email.To);
        Assert.Contains("https://mbfit.online/verify-email#token=", email.TextBody);
        Assert.Contains("https://mbfit.online/verify-email#token=", email.HtmlBody);

        var token = fx.LastToken;
        var row = Assert.Single(fx.Tokens);
        Assert.Equal(AccountTokenPurpose.EmailVerification, row.Purpose);
        Assert.NotEqual(token, row.TokenHash);
        Assert.Equal(AccountTokenGenerator.Hash(token), row.TokenHash);
        Assert.DoesNotContain(fx.Tokens, t => t.TokenHash.Contains(token));
        Assert.Equal(fx.Clock.Now.UtcDateTime.AddHours(24), row.ExpiresAt);
        Assert.Equal($"account-EmailVerification-{row.Id}", key);
    }

    [Fact]
    public async Task Registration_WhenEmailFails_ReturnsFalse_AndLeavesNoToken()
    {
        var fx = new Fixture();
        fx.Sender.FailWith = new EmailDeliveryException(ResendEmailSender.UnavailableMessage, isConfigurationError: false);

        var sent = await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);

        Assert.False(sent);
        Assert.Empty(fx.Tokens);
    }

    [Fact]
    public async Task Registration_WhenApiKeyMissing_ReturnsFalse()
    {
        var fx = new Fixture();
        fx.Sender.IsConfigured = false;

        Assert.False(await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null));
        Assert.Empty(fx.Tokens);
    }

    [Fact]
    public async Task ConfirmEmail_WithValidToken_MarksEmailConfirmed()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);

        var result = await fx.Service.ConfirmEmailAsync(fx.LastToken);

        Assert.True(result.EmailConfirmed);
        Assert.Equal(UserEmail, result.Email);
        Assert.True(fx.User.EmailConfirmed);
        Assert.NotNull(Assert.Single(fx.Tokens).UsedAt);
    }

    [Fact]
    public async Task ConfirmEmail_SameTokenTwice_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var token = fx.LastToken;
        await fx.Service.ConfirmEmailAsync(token);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(token));
        Assert.Equal(AccountEmailService.InvalidLinkMessage, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kisa")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // bicim dogru ama kayitli degil
    public async Task ConfirmEmail_WithInvalidToken_IsRejected(string token)
    {
        var fx = new Fixture();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(token));
        Assert.Equal(AccountEmailService.InvalidLinkMessage, ex.Message);
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_WithExpiredToken_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        fx.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.LastToken));
        Assert.Equal(AccountEmailService.ExpiredLinkMessage, ex.Message);
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_WithPasswordResetToken_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.RequestPasswordResetAsync(UserEmail, null);

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.LastToken));
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_AfterEmailChanged_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        fx.User.Email = "new@example.com";
        fx.User.NormalizedEmail = "NEW@EXAMPLE.COM";

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.LastToken));
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ResendVerification_WhenAlreadyVerified_DoesNotSend()
    {
        var fx = new Fixture(emailConfirmed: true);

        var result = await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);

        Assert.True(result.AlreadyVerified);
        Assert.False(result.Sent);
        Assert.Empty(fx.Sender.Sent);
    }

    [Fact]
    public async Task ResendVerification_WithinCooldown_Returns429WithRetryAfter_ThenAllowsAndRevokesOldLink()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var firstToken = fx.LastToken;
        fx.Clock.Advance(TimeSpan.FromSeconds(20));

        var limited = await Assert.ThrowsAsync<TooManyRequestsException>(() => fx.Service.ResendVerificationEmailAsync(fx.User.Id, null));
        Assert.Equal(TimeSpan.FromSeconds(40), limited.RetryAfter);
        Assert.Single(fx.Sender.Sent);

        fx.Clock.Advance(TimeSpan.FromSeconds(40));
        var result = await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);

        Assert.True(result.Sent);
        Assert.Equal(60, result.ResendAvailableInSeconds);
        Assert.Equal(2, fx.Sender.Sent.Count);
        // Eski baglanti iptal edildi, yenisi calisir.
        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(firstToken));
        await fx.Service.ConfirmEmailAsync(fx.LastToken);
        Assert.True(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ResendVerification_HourlyLimit_IsEnforced()
    {
        var fx = new Fixture();
        fx.Options.MaxEmailsPerHour = 3;
        for (var i = 0; i < 3; i++)
        {
            await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);
            fx.Clock.Advance(TimeSpan.FromMinutes(2));
        }

        var limited = await Assert.ThrowsAsync<TooManyRequestsException>(() => fx.Service.ResendVerificationEmailAsync(fx.User.Id, null));

        // Ilk e-posta 6 dakika once gonderildi; pencere 54 dakika sonra acilir.
        Assert.Equal(TimeSpan.FromMinutes(54), limited.RetryAfter);
        Assert.Equal(3, fx.Sender.Sent.Count);
    }

    [Fact]
    public async Task ResendVerification_WhenEmailFails_Throws503_AndDoesNotConsumeCooldown()
    {
        var fx = new Fixture();
        fx.Sender.FailWith = new EmailDeliveryException(ResendEmailSender.UnavailableMessage, isConfigurationError: false);

        await Assert.ThrowsAsync<EmailDeliveryException>(() => fx.Service.ResendVerificationEmailAsync(fx.User.Id, null));
        Assert.Empty(fx.Tokens);

        fx.Sender.FailWith = null;
        var result = await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);
        Assert.True(result.Sent);
    }

    [Fact]
    public async Task VerificationEmail_EncodesUserInput()
    {
        var fx = new Fixture();

        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "<img src=x onerror=alert(1)>", null);

        var html = fx.Sender.Sent[0].Email.HtmlBody;
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
    }

    // --- Sifremi unuttum ---

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsSameGenericResponse_AndSendsNothing()
    {
        var fx = new Fixture();

        var unknown = await fx.Service.RequestPasswordResetAsync("nobody@example.com", null);
        var known = await fx.Service.RequestPasswordResetAsync("  mert@EXAMPLE.com ", null);

        Assert.Equal(AccountEmailService.ForgotPasswordGenericMessage, unknown.Message);
        Assert.Equal(unknown.Message, known.Message);
        var (email, _) = Assert.Single(fx.Sender.Sent);
        Assert.Equal(UserEmail, email.To);
        Assert.Contains("https://mbfit.online/reset-password#token=", email.TextBody);
        Assert.Equal(fx.Clock.Now.UtcDateTime.AddMinutes(30), Assert.Single(fx.Tokens).ExpiresAt);
    }

    [Fact]
    public async Task ForgotPassword_InactiveAccount_ReturnsGenericResponse_AndSendsNothing()
    {
        var fx = new Fixture();
        fx.User.IsActive = false;

        var result = await fx.Service.RequestPasswordResetAsync(UserEmail, null);

        Assert.Equal(AccountEmailService.ForgotPasswordGenericMessage, result.Message);
        Assert.Empty(fx.Sender.Sent);
    }

    [Fact]
    public async Task ForgotPassword_WithinCooldown_ReturnsGenericResponse_WithoutSecondEmail()
    {
        var fx = new Fixture();
        await fx.Service.RequestPasswordResetAsync(UserEmail, null);
        fx.Clock.Advance(TimeSpan.FromSeconds(10));

        var result = await fx.Service.RequestPasswordResetAsync(UserEmail, null);

        Assert.Equal(AccountEmailService.ForgotPasswordGenericMessage, result.Message);
        Assert.Single(fx.Sender.Sent);
    }

    [Fact]
    public async Task ForgotPassword_WhenApiKeyMissing_FailsForEveryone_BeforeAccountLookup()
    {
        var fx = new Fixture();
        fx.Sender.IsConfigured = false;

        var unknown = await Assert.ThrowsAsync<EmailDeliveryException>(() => fx.Service.RequestPasswordResetAsync("nobody@example.com", null));
        var known = await Assert.ThrowsAsync<EmailDeliveryException>(() => fx.Service.RequestPasswordResetAsync(UserEmail, null));

        Assert.True(unknown.IsConfigurationError);
        Assert.Equal(unknown.Message, known.Message);
        Assert.Empty(fx.Tokens);
    }

    [Fact]
    public async Task ForgotPassword_WhenResendFails_DoesNotReportSuccess()
    {
        var fx = new Fixture();
        fx.Sender.FailWith = new EmailDeliveryException(ResendEmailSender.UnavailableMessage, isConfigurationError: false);

        await Assert.ThrowsAsync<EmailDeliveryException>(() => fx.Service.RequestPasswordResetAsync(UserEmail, null));
        Assert.Empty(fx.Tokens);
    }

    // --- Yeni sifre belirleme ---

    private static async Task<string> RequestResetTokenAsync(Fixture fx)
    {
        await fx.Service.RequestPasswordResetAsync(UserEmail, null);
        return fx.LastToken;
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesPassword_RevokesSessions_AndSendsNotice()
    {
        var fx = new Fixture();
        fx.Db.Set<UserRefreshToken>().AddRange(new[]
        {
            new UserRefreshToken { Id = 1, UserId = fx.User.Id, Token = "a", ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new UserRefreshToken { Id = 2, UserId = fx.User.Id, Token = "b", ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new UserRefreshToken { Id = 3, UserId = 999, Token = "c", ExpiresAt = DateTime.UtcNow.AddDays(10) },
        });
        var token = await RequestResetTokenAsync(fx);
        await fx.Service.ValidatePasswordResetTokenAsync(token);

        var result = await fx.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = token, NewPassword = "new-password", ConfirmPassword = "new-password" }, "9.9.9.9");

        Assert.Equal(AccountEmailService.PasswordResetSuccessMessage, result.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-password", fx.User.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
        Assert.True(fx.User.EmailConfirmed);
        Assert.All(fx.Db.Set<UserRefreshToken>().Where(t => t.UserId == fx.User.Id), t =>
        {
            Assert.True(t.IsRevoked);
            Assert.Equal("9.9.9.9", t.RevokedByIp);
        });
        Assert.False(fx.Db.Set<UserRefreshToken>().Single(t => t.UserId == 999).IsRevoked);
        Assert.Equal("MB FIT şifren değiştirildi", fx.Sender.Sent[^1].Email.Subject);
        Assert.DoesNotContain("new-password", fx.Sender.Sent[^1].Email.HtmlBody);
    }

    [Fact]
    public async Task ResetPassword_SameTokenTwice_IsRejected()
    {
        var fx = new Fixture();
        var token = await RequestResetTokenAsync(fx);
        await fx.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = token, NewPassword = "new-password", ConfirmPassword = "new-password" }, null);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = token, NewPassword = "another-pass", ConfirmPassword = "another-pass" }, null));

        Assert.Equal(AccountEmailService.InvalidLinkMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-password", fx.User.PasswordHash));
        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ValidatePasswordResetTokenAsync(token));
    }

    [Fact]
    public async Task ResetPassword_WithWrongToken_IsRejected()
    {
        var fx = new Fixture();
        await RequestResetTokenAsync(fx);
        var (wrongToken, _) = AccountTokenGenerator.Create();

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = wrongToken, NewPassword = "new-password", ConfirmPassword = "new-password" }, null));
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_IsRejected()
    {
        var fx = new Fixture();
        var token = await RequestResetTokenAsync(fx);
        fx.Clock.Advance(TimeSpan.FromMinutes(31));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = token, NewPassword = "new-password", ConfirmPassword = "new-password" }, null));
        Assert.Equal(AccountEmailService.ExpiredLinkMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Theory]
    [InlineData("new-password", "different", "ConfirmPassword")]
    [InlineData("12345", "12345", "NewPassword")]
    public async Task ResetPassword_InvalidNewPassword_IsRejected_AndTokenStaysUsable(string password, string confirm, string field)
    {
        var fx = new Fixture();
        var token = await RequestResetTokenAsync(fx);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Token = token, NewPassword = password, ConfirmPassword = confirm }, null));

        Assert.True(ex.Errors.ContainsKey(field));
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
        await fx.Service.ValidatePasswordResetTokenAsync(token);
    }

    [Fact]
    public async Task NewResetRequest_RevokesPreviousResetLink()
    {
        var fx = new Fixture();
        var first = await RequestResetTokenAsync(fx);
        fx.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await RequestResetTokenAsync(fx);

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ValidatePasswordResetTokenAsync(first));
        await fx.Service.ValidatePasswordResetTokenAsync(second);
    }

    [Fact]
    public void GeneratedTokens_AreUnique_UrlSafe_AndWellFormed()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => AccountTokenGenerator.Create().Token).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.True(AccountTokenGenerator.HasValidFormat(t)));
        Assert.All(tokens, t => Assert.Equal(Uri.EscapeDataString(t), t));
    }
}
