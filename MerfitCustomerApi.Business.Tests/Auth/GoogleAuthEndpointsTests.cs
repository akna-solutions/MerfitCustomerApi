using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Auth;

/// <summary>
/// Google uclarinin gercek ASP.NET Core pipeline'i uzerinden davranisi: oturum gerektirmez, istemci kimlikleri
/// yokken uygulama acilir ve 503 doner, bos token 400, yanit gövdesi camelCase.
/// </summary>
public class GoogleAuthEndpointsTests : IClassFixture<WebApplicationFactory<AuthController>>
{
    private readonly WebApplicationFactory<AuthController> _baseFactory;

    public GoogleAuthEndpointsTests(WebApplicationFactory<AuthController> factory)
    {
        _baseFactory = factory;
    }

    private WebApplicationFactory<AuthController> CreateFactory(Mock<IAuthService>? auth = null) =>
        _baseFactory.WithWebHostBuilder(builder => builder
            .UseSetting("Resend:ApiKey", string.Empty)
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                if (auth is not null) services.AddScoped(_ => auth.Object);
            }));

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task GoogleSignIn_WithoutClientIds_AppStartsAndReturns503()
    {
        // Gercek servisler: GoogleAuth:ClientIds bos. Kontrol hesap aranmadan once yapildigi icin veritabani gerekmez.
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new { idToken = "abc" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("Google ile giriş şu anda kullanılamıyor.", body.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task GoogleSignIn_WithoutToken_Returns400()
    {
        var auth = new Mock<IAuthService>();
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new { idToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        auth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GoogleSignIn_RegistrationRequired_ReturnsProfile()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.GoogleSignInAsync(It.Is<GoogleSignInRequest>(r => r.IdToken == "tok"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleSignInResponse
            {
                RegistrationRequired = true,
                Profile = new GoogleProfileDto { Email = "mert@gmail.com", FirstName = "Mert" },
            });
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google", new { idToken = "tok" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.True(body.GetProperty("registrationRequired").GetBoolean());
        Assert.Equal("mert@gmail.com", body.GetProperty("profile").GetProperty("email").GetString());
    }

    [Fact]
    public async Task GoogleRegister_Returns201ForNewAccount_And200ForExisting()
    {
        var auth = new Mock<IAuthService>();
        auth.SetupSequence(a => a.GoogleRegisterAsync(It.IsAny<GoogleRegisterRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponse { UserId = 7, IsNewUser = true, EmailConfirmed = true })
            .ReturnsAsync(new AuthResponse { UserId = 7, IsNewUser = false, EmailConfirmed = true });
        using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        var request = new { idToken = "tok", firstName = "Mert", lastName = "Akpınar", username = "mert_g", heightUnit = "cm", heightCm = 180, weightUnit = "kg", weight = 78 };

        var created = await client.PostAsJsonAsync("/api/auth/google/register", request);
        var existing = await client.PostAsJsonAsync("/api/auth/google/register", request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True((await BodyAsync(created)).GetProperty("isNewUser").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
    }

    [Fact]
    public async Task GoogleRegister_WithInvalidUsername_Returns400()
    {
        var auth = new Mock<IAuthService>();
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google/register",
            new { idToken = "tok", firstName = "Mert", lastName = "Akpınar", username = "a b" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        auth.VerifyNoOtherCalls();
    }
}
