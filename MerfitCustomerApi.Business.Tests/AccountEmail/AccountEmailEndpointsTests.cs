using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// Yeni auth uclarinin gercek ASP.NET Core pipeline'i uzerinden davranisi: kimlik dogrulama gereksinimi,
/// ApiResponse hata govdesi, 429 + Retry-After, IP hiz siniri (giris/kayit etkilenmez), eksik API anahtari
/// ile uygulamanin acilmasi ve e-posta baglanti sayfalarinin guvenlik basliklari.
/// </summary>
public class AccountEmailEndpointsTests : IClassFixture<WebApplicationFactory<AuthController>>
{
    private readonly WebApplicationFactory<AuthController> _baseFactory;

    public AccountEmailEndpointsTests(WebApplicationFactory<AuthController> factory)
    {
        _baseFactory = factory;
    }

    private WebApplicationFactory<AuthController> CreateFactory(
        Mock<IAccountEmailService>? accountEmails = null,
        Mock<IAuthService>? auth = null,
        int perIpLimit = 1000) =>
        _baseFactory.WithWebHostBuilder(builder => builder
            .UseSetting("AccountEmail:PerIpRequestsPerWindow", perIpLimit.ToString())
            // Gelistiricinin User Secrets'indaki gercek anahtar testleri etkilemesin.
            .UseSetting("Resend:ApiKey", string.Empty)
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                if (accountEmails is not null) services.AddScoped(_ => accountEmails.Object);
                if (auth is not null) services.AddScoped(_ => auth.Object);
            }));

    private static HttpClient Authorized(WebApplicationFactory<AuthController> factory, long userId)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var (token, _) = scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateAccessToken(new ApplicationUser { Id = userId, Email = "u@test.local", Role = UserRole.User });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task AppStarts_WithoutResendApiKey_AndForgotPasswordReturnsClear503()
    {
        // Gercek servisler: anahtar tanimli degil. Kontrol hesap aranmadan once yapildigi icin veritabani gerekmez.
        using var factory = CreateFactory();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.False(scope.ServiceProvider.GetRequiredService<IEmailSender>().IsConfigured);
            Assert.IsType<AccountEmailService>(scope.ServiceProvider.GetRequiredService<IAccountEmailService>());
        }

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/forgot", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(ResendEmailSender.NotConfiguredMessage, body.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task ForgotPassword_ReturnsGenericMessage()
    {
        var service = new Mock<IAccountEmailService>();
        service.Setup(s => s.RequestPasswordResetAsync("x@example.com", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { Message = AccountEmailService.ForgotPasswordGenericMessage });
        using var factory = CreateFactory(service);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/forgot", new { email = "x@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AccountEmailService.ForgotPasswordGenericMessage, (await BodyAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ForgotPassword_WithInvalidEmail_Returns400()
    {
        var service = new Mock<IAccountEmailService>();
        using var factory = CreateFactory(service);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/password/forgot", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResendVerification_RequiresAuthentication()
    {
        var service = new Mock<IAccountEmailService>();
        using var factory = CreateFactory(service);

        var response = await factory.CreateClient().PostAsync("/api/auth/email-verification/resend", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResendVerification_UsesUserFromToken_AndCooldownReturns429WithRetryAfter()
    {
        var service = new Mock<IAccountEmailService>();
        service.SetupSequence(s => s.ResendVerificationEmailAsync(42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResendVerificationEmailResponse { Sent = true, ResendAvailableInSeconds = 60 })
            .ThrowsAsync(new TooManyRequestsException(AccountEmailService.ResendCooldownMessage, TimeSpan.FromSeconds(45)));
        using var factory = CreateFactory(service);
        var client = Authorized(factory, 42);

        var first = await client.PostAsync("/api/auth/email-verification/resend", null);
        var second = await client.PostAsync("/api/auth/email-verification/resend", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(60, (await BodyAsync(first)).GetProperty("resendAvailableInSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(45), second.Headers.RetryAfter?.Delta);
        Assert.Equal(AccountEmailService.ResendCooldownMessage, (await BodyAsync(second)).GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task ConfirmEmail_InvalidToken_Returns400WithMessage()
    {
        var service = new Mock<IAccountEmailService>();
        service.Setup(s => s.ConfirmEmailAsync("bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppValidationException("token", AccountEmailService.InvalidLinkMessage));
        using var factory = CreateFactory(service);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/email-verification/confirm", new { token = "bad" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AccountEmailService.InvalidLinkMessage, (await BodyAsync(response)).GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task EmailDeliveryFailure_Returns503_NotSuccess()
    {
        var service = new Mock<IAccountEmailService>();
        service.Setup(s => s.ResendVerificationEmailAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailDeliveryException(ResendEmailSender.UnavailableMessage, isConfigurationError: false));
        using var factory = CreateFactory(service);

        var response = await Authorized(factory, 5).PostAsync("/api/auth/email-verification/resend", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False((await BodyAsync(response)).GetProperty("isSuccess").GetBoolean());
    }

    [Fact]
    public async Task PerIpRateLimit_Returns429_AndDoesNotAffectLoginOrOtherEndpoints()
    {
        var service = new Mock<IAccountEmailService>();
        service.Setup(s => s.RequestPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { Message = "ok" });
        service.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { Message = "ok" });
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>())).ReturnsAsync(new AuthResponse { AccessToken = "a" });
        using var factory = CreateFactory(service, auth, perIpLimit: 2);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password/forgot", new { email = "a@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password/forgot", new { email = "b@example.com" })).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/auth/password/forgot", new { email = "c@example.com" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        Assert.Contains("Çok fazla deneme", (await BodyAsync(limited)).GetProperty("errorMessage").GetString());
        service.Verify(s => s.RequestPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Her uc kendi penceresine sahip; giris ucu hic sinirlanmaz.
        var reset = await client.PostAsJsonAsync("/api/auth/password/reset", new { token = "t", newPassword = "123456", confirmPassword = "123456" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        for (var i = 0; i < 4; i++)
        {
            var login = await client.PostAsJsonAsync("/api/auth/login", new { emailOrUsername = "mert", password = "password1" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }

    [Theory]
    [InlineData("/verify-email")]
    [InlineData("/reset-password")]
    public async Task LinkPages_AreServedWithSecurityHeaders_AndChangeNothing(string path)
    {
        var service = new Mock<IAccountEmailService>(MockBehavior.Strict);
        using var factory = CreateFactory(service);

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("form-action 'none'", csp);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("mbfitnativeapp", html);
        Assert.Contains("location.hash", html);
        // Sayfa acilisi servisi cagirmaz (Strict mock: herhangi bir cagri hata olurdu).
    }
}
