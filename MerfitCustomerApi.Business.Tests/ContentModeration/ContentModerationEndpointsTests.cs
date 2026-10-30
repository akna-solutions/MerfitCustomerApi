using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.ContentModeration;

/// <summary>
/// Moderasyonun mobil uygulamaya degil backend'e bagli oldugunu gercek ASP.NET Core pipeline'i uzerinden dogrular:
/// API'ye dogrudan gonderilen uygunsuz ad/soyad/kullanici adi 400 + standart ApiResponse ile reddedilir ve
/// veritabanina hicbir sey yazilmaz. Servisler (AuthService, ProfileService, ContentModerationService) gercektir;
/// yalnizca veritabani bellekteki <see cref="InMemoryUnitOfWork"/> ile degistirilir.
/// </summary>
public class ContentModerationEndpointsTests : IClassFixture<WebApplicationFactory<AuthController>>
{
    private const long UserId = 42;

    private readonly WebApplicationFactory<AuthController> _baseFactory;
    private readonly InMemoryUnitOfWork _db = new();

    public ContentModerationEndpointsTests(WebApplicationFactory<AuthController> factory)
    {
        _db.Register<ApplicationUser>().Register<UserProfile>().Register<UserRefreshToken>().Register<ExternalLogin>();
        _baseFactory = factory;
    }

    private WebApplicationFactory<AuthController> CreateFactory(IGoogleIdTokenValidator? googleValidator = null) =>
        _baseFactory.WithWebHostBuilder(builder => builder
            .UseSetting("Resend:ApiKey", string.Empty)
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                services.AddScoped<IUnitOfWork>(_ => _db.Object);
                if (googleValidator is not null) services.AddSingleton(googleValidator);
            }));

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<AuthController> factory)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = UserId, Email = "mert@example.com", Role = UserRole.User });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static object RegisterBody(string firstName = "Mert", string lastName = "Akpınar", string username = "mert") => new
    {
        firstName,
        lastName,
        username,
        email = "mert@example.com",
        password = "password1",
        confirmPassword = "password1",
        heightUnit = "cm",
        heightCm = 180,
        weightUnit = "kg",
        weight = 78,
    };

    private void SeedUser()
    {
        _db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = UserId,
            Email = "mert@example.com",
            NormalizedEmail = "MERT@EXAMPLE.COM",
            UserName = "mert@example.com",
            NormalizedUserName = "MERT@EXAMPLE.COM",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true,
        });
        _db.Set<UserProfile>().Add(new UserProfile { Id = 1, UserId = UserId, FirstName = "Mert", LastName = "Akpınar", Username = "mert" });
    }

    // 11. Kayit endpoint'i uygunsuz icerigi reddetmeli
    [Theory]
    [InlineData("Mert", "Akpınar", "orospu_mert", "Kullanıcı adı küfür içeremez")]
    [InlineData("Mert", "Akpınar", "admin", "Bu kullanıcı adı sistem hesaplarına ayrılmıştır")]
    [InlineData("Mert", "Akpınar", "mbfit_support", "Bu kullanıcı adı sistem hesaplarına ayrılmıştır")]
    [InlineData("s.i.k.t.i.r", "Akpınar", "mert", "Ad küfür içeremez")]
    [InlineData("Mert", "ŞEREFSİZ", "mert", "Soyad hakaret içeremez")]
    public async Task Register_WithInappropriateContent_Returns400AndSavesNothing(string firstName, string lastName, string username, string expectedMessage)
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", RegisterBody(firstName, lastName, username));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.StartsWith(expectedMessage, body.GetProperty("errorMessage").GetString());
        Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetString()!.StartsWith(expectedMessage));
        Assert.Empty(_db.Set<ApplicationUser>());
        Assert.Empty(_db.Set<UserProfile>());
        Assert.Equal(0, _db.SaveCount);
        _db.Mock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_WithSeveralInappropriateFields_ReportsEachField()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", RegisterBody("amk", "orospu", "admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await BodyAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("Ad "));
        Assert.Contains(errors, e => e.StartsWith("Soyad "));
        Assert.Contains(errors, e => e.StartsWith("Bu kullanıcı adı"));
    }

    [Fact]
    public async Task GoogleRegister_WithInappropriateUsername_Returns400AndSavesNothing()
    {
        var google = new Mock<IGoogleIdTokenValidator>();
        google.Setup(g => g.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdentity("google-sub", "mert@gmail.com", true, "Mert", "Akpınar"));
        using var factory = CreateFactory(google.Object);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/google/register",
            new { idToken = "tok", firstName = "Mert", lastName = "Akpınar", username = "s1kt1r_mert", heightUnit = "cm", heightCm = 180, weightUnit = "kg", weight = 78 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("Kullanıcı adı küfür içeremez", (await BodyAsync(response)).GetProperty("errorMessage").GetString());
        Assert.Empty(_db.Set<ApplicationUser>());
        Assert.Empty(_db.Set<ExternalLogin>());
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("amk_123")]
    public async Task UsernameAvailability_ReportsInappropriateUsernameAsUnavailable(string username)
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync($"/api/auth/username-availability?username={username}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
    }

    // 10. Profil guncelleme endpoint'i uygunsuz icerigi reddetmeli
    [Theory]
    [InlineData("{\"username\":\"administrator\"}", "Bu kullanıcı adı sistem hesaplarına ayrılmıştır")]
    [InlineData("{\"username\":\"yavsak_34\"}", "Kullanıcı adı küfür içeremez")]
    [InlineData("{\"firstName\":\"O R O S P U\"}", "Ad küfür içeremez")]
    [InlineData("{\"lastName\":\"gerizekalı\",\"theme\":\"dark\"}", "Soyad hakaret içeremez")]
    public async Task UpdateProfile_WithInappropriateContent_Returns400AndKeepsProfile(string json, string expectedMessage)
    {
        SeedUser();
        using var factory = CreateFactory();
        var client = CreateAuthenticatedClient(factory);

        var response = await client.PutAsync("/api/profile", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.StartsWith(expectedMessage, body.GetProperty("errorMessage").GetString());

        var profile = Assert.Single(_db.Set<UserProfile>());
        Assert.Equal("Mert", profile.FirstName);
        Assert.Equal("Akpınar", profile.LastName);
        Assert.Equal("mert", profile.Username);
        Assert.Equal(0, _db.SaveCount);
    }
}
