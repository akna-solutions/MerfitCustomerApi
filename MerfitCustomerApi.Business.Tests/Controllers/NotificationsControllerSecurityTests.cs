using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// Bildirim ve cihaz uc noktalarinin kimlik dogrulamasi gerektirdigini ve kullanici kimliginin her zaman
/// JWT'den alindigini (govdeden/sorgudan degil) gercek pipeline uzerinden dogrular.
/// </summary>
public class NotificationsControllerSecurityTests : IClassFixture<WebApplicationFactory<NotificationsController>>
{
    private readonly WebApplicationFactory<NotificationsController> _factory;
    private readonly Mock<INotificationDeviceService> _devices = new();
    private readonly Mock<INotificationService> _notifications = new();

    public NotificationsControllerSecurityTests(WebApplicationFactory<NotificationsController> factory)
    {
        _devices.Setup(s => s.RegisterAsync(It.IsAny<long>(), It.IsAny<RegisterDeviceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerDeviceRegistrationDto { Id = 1, Platform = "ios", IsActive = true });
        _notifications.Setup(s => s.GetPreferencesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerNotificationPreferencesDto { PushEnabled = true, EffectiveTimeZone = "Europe/Istanbul" });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
            services.AddScoped(_ => _devices.Object);
            services.AddScoped(_ => _notifications.Object);
        }));
    }

    private HttpClient CreateClient(long? userId)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = userId.Value, Email = "u@test.local", Role = UserRole.User });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Theory]
    [InlineData("GET", "/api/notifications")]
    [InlineData("GET", "/api/notifications/preferences")]
    [InlineData("PUT", "/api/notifications/devices")]
    [InlineData("POST", "/api/notifications/devices/deactivate")]
    public async Task AnonymousRequests_AreRejectedWith401(string method, string url)
    {
        var client = CreateClient(null);
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { token = "ExponentPushToken[x]", platform = "ios" }),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_UsesAuthenticatedUserId_IgnoringClientSuppliedUserId()
    {
        var client = CreateClient(42);

        var response = await client.PutAsync("/api/notifications/devices",
            JsonContent.Create(new { userId = 7, token = "ExponentPushToken[abcdefghijkl]", platform = "ios" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _devices.Verify(s => s.RegisterAsync(42, It.IsAny<RegisterDeviceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _devices.Verify(s => s.RegisterAsync(7, It.IsAny<RegisterDeviceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateDevice_UsesAuthenticatedUserId()
    {
        var client = CreateClient(42);

        var response = await client.PostAsync("/api/notifications/devices/deactivate", JsonContent.Create(new { token = "ExponentPushToken[abcdefghijkl]" }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _devices.Verify(s => s.DeactivateAsync(42, It.IsAny<DeactivateDeviceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Preferences_AreReadForAuthenticatedUser()
    {
        var client = CreateClient(42);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notifications/preferences")).StatusCode);
        _notifications.Verify(s => s.GetPreferencesAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }
}
