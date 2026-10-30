using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Auth;

/// <summary>
/// /api/auth/refresh ve /api/auth/logout uclarinin gercek pipeline uzerinden davranisi: access token gerektirmez
/// (suresi dolmus token'la cagrilir), gecersiz refresh token 401, bos govde 400, cikis 204.
/// </summary>
public class RefreshEndpointsTests : IClassFixture<WebApplicationFactory<AuthController>>
{
    private readonly WebApplicationFactory<AuthController> _baseFactory;

    public RefreshEndpointsTests(WebApplicationFactory<AuthController> factory)
    {
        _baseFactory = factory;
    }

    private WebApplicationFactory<AuthController> CreateFactory(Mock<IAuthService> auth) =>
        _baseFactory.WithWebHostBuilder(builder => builder
            .UseSetting("Resend:ApiKey", string.Empty)
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                services.AddScoped(_ => auth.Object);
            }));

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewPairWithoutBearer()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RefreshAsync(It.Is<RefreshTokenRequest>(r => r.RefreshToken == "r1"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponse { UserId = 5, AccessToken = "a2", RefreshToken = "r2" });
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "r1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("a2", body.GetProperty("accessToken").GetString());
        Assert.Equal("r2", body.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("Oturumunun süresi doldu. Lütfen tekrar giriş yap."));
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "bad" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutToken_Returns400()
    {
        var auth = new Mock<IAuthService>();
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        auth.Verify(a => a.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Logout_Returns204()
    {
        var auth = new Mock<IAuthService>();
        using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = "r1" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        auth.Verify(a => a.LogoutAsync(It.Is<RefreshTokenRequest>(r => r.RefreshToken == "r1"), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
