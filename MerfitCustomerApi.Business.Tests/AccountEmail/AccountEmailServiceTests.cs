using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama is kurallari: 6 haneli tek kullanimlik/sureli kod, yalnizca ozetin
/// saklanmasi, kod basina deneme siniri, hesap kesfini onleyen genel yanit, bekleme suresi ve saatlik sinir,
/// sifre degisince oturumlarin kapanmasi.
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

        public string LastCode => FakeEmailSender.ExtractCode(Sender.Sent[^1].Email);

        /// <summary>Son koddan farkli, ayni bicimde bir kod.</summary>
        public string WrongCode => ((int.Parse(LastCode) + 1) % 1_000_000).ToString("D6");
    }

    private static ResetPasswordRequest Reset(string code, string password = "new-password", string? confirm = null, string email = UserEmail) =>
        new() { Email = email, Code = code, NewPassword = password, ConfirmPassword = confirm ?? password };

    // --- E-posta dogrulama ---

    [Fact]
    public async Task Registration_SendsVerificationCode_AndStoresOnlyCodeHash()
    {
        var fx = new Fixture();

        var sent = await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", "1.2.3.4");

        Assert.True(sent);
        var (email, key) = Assert.Single(fx.Sender.Sent);
        Assert.Equal(UserEmail, email.To);
        var code = fx.LastCode;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Contains(code, email.HtmlBody);
        // Kod konu satirinda yok (kilit ekrani bildirimlerinde gorunmesin).
        Assert.DoesNotContain(code, email.Subject);
        Assert.DoesNotContain("http", email.TextBody);

        var row = Assert.Single(fx.Tokens);
        Assert.Equal(AccountTokenPurpose.EmailVerification, row.Purpose);
        Assert.NotEqual(code, row.TokenHash);
        Assert.Equal(AccountTokenGenerator.Hash(fx.User.Id, AccountTokenPurpose.EmailVerification, code), row.TokenHash);
        Assert.Equal(fx.Clock.Now.UtcDateTime.AddMinutes(15), row.ExpiresAt);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal($"account-EmailVerification-{row.Id}", key);
    }

    [Fact]
    public async Task Registration_WhenEmailFails_ReturnsFalse_AndLeavesNoCode()
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ConfirmEmail_WithCorrectCode_MarksEmailConfirmed(bool withSpaces, bool withDash)
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var code = fx.LastCode;
        if (withSpaces) code = $" {code[..3]} {code[3..]} ";
        if (withDash) code = $"{code[..3]}-{code[3..]}";

        var result = await fx.Service.ConfirmEmailAsync(fx.User.Id, code);

        Assert.True(result.EmailConfirmed);
        Assert.Equal(UserEmail, result.Email);
        Assert.True(fx.User.EmailConfirmed);
        Assert.NotNull(Assert.Single(fx.Tokens).UsedAt);
    }

    [Fact]
    public async Task ConfirmEmail_WhenAlreadyVerified_ReturnsConfirmed_WithoutCode()
    {
        var fx = new Fixture(emailConfirmed: true);

        var result = await fx.Service.ConfirmEmailAsync(fx.User.Id, "000000");

        Assert.True(result.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_SameCodeTwice_AfterEmailChangedBack_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var code = fx.LastCode;
        await fx.Service.ConfirmEmailAsync(fx.User.Id, code);
        // E-posta degisince dogrulama sifirlanir; eski kod tekrar kullanilamaz.
        fx.User.EmailConfirmed = false;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, code));
        Assert.Equal(AccountEmailService.NoActiveCodeMessage, ex.Message);
    }

    [Fact]
    public async Task ConfirmEmail_WrongCode_IsRejected_AndCountsAttempt()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, fx.WrongCode));

        Assert.Equal(AccountEmailService.InvalidCodeMessage, ex.Message);
        Assert.Equal(1, Assert.Single(fx.Tokens).AttemptCount);
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_AfterMaxWrongAttempts_EvenCorrectCodeIsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var code = fx.LastCode;

        for (var i = 1; i <= fx.Options.MaxCodeAttempts; i++)
        {
            var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, fx.WrongCode));
            Assert.Equal(i == fx.Options.MaxCodeAttempts ? AccountEmailService.TooManyAttemptsMessage : AccountEmailService.InvalidCodeMessage, ex.Message);
        }

        var blocked = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, code));
        Assert.Equal(AccountEmailService.TooManyAttemptsMessage, blocked.Message);
        Assert.False(fx.User.EmailConfirmed);
        Assert.Equal(fx.Options.MaxCodeAttempts, Assert.Single(fx.Tokens).AttemptCount);

        // Yeni kod istenince deneme hakki yenilenir.
        fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);
        await fx.Service.ConfirmEmailAsync(fx.User.Id, fx.LastCode);
        Assert.True(fx.User.EmailConfirmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("<script>")]
    public async Task ConfirmEmail_MalformedCode_IsRejected_WithoutUsingAnAttempt(string code)
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, code));

        Assert.Equal(0, Assert.Single(fx.Tokens).AttemptCount);
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_WithoutAnyCode_IsRejected()
    {
        var fx = new Fixture();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, "123456"));
        Assert.Equal(AccountEmailService.NoActiveCodeMessage, ex.Message);
    }

    [Fact]
    public async Task ConfirmEmail_WithExpiredCode_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        fx.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, fx.LastCode));
        Assert.Equal(AccountEmailService.ExpiredCodeMessage, ex.Message);
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_WithPasswordResetCode_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.RequestPasswordResetAsync(UserEmail, null);

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, fx.LastCode));
        Assert.False(fx.User.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_CodeOfAnotherUser_IsRejected()
    {
        var fx = new Fixture();
        var other = new ApplicationUser { Id = 8, Email = "o@example.com", NormalizedEmail = "O@EXAMPLE.COM", IsActive = true };
        fx.Db.Set<ApplicationUser>().Add(other);
        await fx.Service.TrySendVerificationAfterRegistrationAsync(other, "O", null);
        var othersCode = fx.LastCode;
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);

        // Kod ozeti kullaniciya baglidir: baska bir hesabin kodu bu hesapta calismaz.
        if (othersCode != fx.LastCode)
        {
            await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, othersCode));
            Assert.False(fx.User.EmailConfirmed);
        }

        Assert.False(other.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_AfterEmailChanged_IsRejected()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        fx.User.Email = "new@example.com";
        fx.User.NormalizedEmail = "NEW@EXAMPLE.COM";

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, fx.LastCode));
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
    public async Task ResendVerification_WithinCooldown_Returns429WithRetryAfter_ThenAllowsAndRevokesOldCode()
    {
        var fx = new Fixture();
        await fx.Service.TrySendVerificationAfterRegistrationAsync(fx.User, "Mert", null);
        var firstCode = fx.LastCode;
        fx.Clock.Advance(TimeSpan.FromSeconds(20));

        var limited = await Assert.ThrowsAsync<TooManyRequestsException>(() => fx.Service.ResendVerificationEmailAsync(fx.User.Id, null));
        Assert.Equal(TimeSpan.FromSeconds(40), limited.RetryAfter);
        Assert.Single(fx.Sender.Sent);

        fx.Clock.Advance(TimeSpan.FromSeconds(40));
        var result = await fx.Service.ResendVerificationEmailAsync(fx.User.Id, null);

        Assert.True(result.Sent);
        Assert.Equal(60, result.ResendAvailableInSeconds);
        Assert.Equal(2, fx.Sender.Sent.Count);
        // Eski kod iptal edildi, yalnizca yenisi calisir.
        Assert.NotNull(fx.Tokens.Single(t => t.Id == fx.Tokens.Min(x => x.Id)).RevokedAt);
        if (firstCode != fx.LastCode)
        {
            await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ConfirmEmailAsync(fx.User.Id, firstCode));
        }

        await fx.Service.ConfirmEmailAsync(fx.User.Id, fx.LastCode);
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
        Assert.Matches("^[0-9]{6}$", fx.LastCode);
        Assert.Equal(fx.Clock.Now.UtcDateTime.AddMinutes(15), Assert.Single(fx.Tokens).ExpiresAt);
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

    // --- Kod dogrulama ve yeni sifre ---

    private static async Task<string> RequestResetCodeAsync(Fixture fx)
    {
        await fx.Service.RequestPasswordResetAsync(UserEmail, null);
        return fx.LastCode;
    }

    [Fact]
    public async Task VerifyResetCode_UnknownEmail_LooksLikeNoCode()
    {
        var fx = new Fixture();

        var unknown = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.VerifyPasswordResetCodeAsync("nobody@example.com", "123456"));
        var knownWithoutCode = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.VerifyPasswordResetCodeAsync(UserEmail, "123456"));

        Assert.Equal(knownWithoutCode.Message, unknown.Message);
    }

    [Fact]
    public async Task ResetPassword_WithCorrectCode_ChangesPassword_RevokesSessions_AndSendsNotice()
    {
        var fx = new Fixture();
        fx.Db.Set<UserRefreshToken>().AddRange(new[]
        {
            new UserRefreshToken { Id = 1, UserId = fx.User.Id, Token = "a", ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new UserRefreshToken { Id = 2, UserId = fx.User.Id, Token = "b", ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new UserRefreshToken { Id = 3, UserId = 999, Token = "c", ExpiresAt = DateTime.UtcNow.AddDays(10) },
        });
        var code = await RequestResetCodeAsync(fx);
        await fx.Service.VerifyPasswordResetCodeAsync(" mert@example.COM ", code);

        var result = await fx.Service.ResetPasswordAsync(Reset(code), "9.9.9.9");

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
    public async Task ResetPassword_SameCodeTwice_IsRejected()
    {
        var fx = new Fixture();
        var code = await RequestResetCodeAsync(fx);
        await fx.Service.ResetPasswordAsync(Reset(code), null);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(code, "another-pass"), null));

        Assert.Equal(AccountEmailService.NoActiveCodeMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-password", fx.User.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithWrongCode_IsRejected()
    {
        var fx = new Fixture();
        await RequestResetCodeAsync(fx);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(fx.WrongCode), null));

        Assert.Equal(AccountEmailService.InvalidCodeMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WrongEmail_CannotUseSomeoneElsesCode()
    {
        var fx = new Fixture();
        var code = await RequestResetCodeAsync(fx);

        await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(code, email: "nobody@example.com"), null));
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithExpiredCode_IsRejected()
    {
        var fx = new Fixture();
        var code = await RequestResetCodeAsync(fx);
        fx.Clock.Advance(TimeSpan.FromMinutes(16));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(code), null));
        Assert.Equal(AccountEmailService.ExpiredCodeMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_AfterMaxWrongAttempts_IsBlocked()
    {
        var fx = new Fixture();
        var code = await RequestResetCodeAsync(fx);
        for (var i = 0; i < fx.Options.MaxCodeAttempts; i++)
        {
            await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.VerifyPasswordResetCodeAsync(UserEmail, fx.WrongCode));
        }

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(code), null));
        Assert.Equal(AccountEmailService.TooManyAttemptsMessage, ex.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
    }

    [Theory]
    [InlineData("new-password", "different", "ConfirmPassword")]
    [InlineData("12345", "12345", "NewPassword")]
    public async Task ResetPassword_InvalidNewPassword_IsRejected_WithoutUsingAnAttempt(string password, string confirm, string field)
    {
        var fx = new Fixture();
        var code = await RequestResetCodeAsync(fx);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => fx.Service.ResetPasswordAsync(Reset(code, password, confirm), null));

        Assert.True(ex.Errors.ContainsKey(field));
        Assert.True(BCrypt.Net.BCrypt.Verify("old-password", fx.User.PasswordHash));
        Assert.Equal(0, Assert.Single(fx.Tokens).AttemptCount);
        await fx.Service.VerifyPasswordResetCodeAsync(UserEmail, code);
    }

    [Fact]
    public async Task NewResetRequest_RevokesPreviousResetCode()
    {
        var fx = new Fixture();
        await RequestResetCodeAsync(fx);
        fx.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await RequestResetCodeAsync(fx);

        Assert.Single(fx.Tokens, t => t.RevokedAt is null);
        await fx.Service.VerifyPasswordResetCodeAsync(UserEmail, second);
    }

    [Fact]
    public void GeneratedCodes_AreSixDigits_AndHashIsBoundToUserAndPurpose()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => AccountTokenGenerator.CreateCode(1, AccountTokenPurpose.EmailVerification).Code).ToList();

        Assert.All(codes, c => Assert.Matches("^[0-9]{6}$", c));
        Assert.True(codes.Distinct().Count() > 450);
        Assert.NotEqual(
            AccountTokenGenerator.Hash(1, AccountTokenPurpose.EmailVerification, "123456"),
            AccountTokenGenerator.Hash(2, AccountTokenPurpose.EmailVerification, "123456"));
        Assert.NotEqual(
            AccountTokenGenerator.Hash(1, AccountTokenPurpose.EmailVerification, "123456"),
            AccountTokenGenerator.Hash(1, AccountTokenPurpose.PasswordReset, "123456"));
    }
}
