using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Services.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class NotificationDeviceServiceTests
{
    private const string Token = "ExponentPushToken[abcdefghijklmnop]";

    private static (NotificationTestData Data, NotificationDeviceService Service) Create()
    {
        var data = new NotificationTestData();
        data.AddUser(1, withDevice: false);
        data.AddUser(2, withDevice: false);
        return (data, new NotificationDeviceService(data.Db.Object, data.Push));
    }

    [Fact]
    public async Task Register_NewToken_CreatesActiveDeviceForAuthenticatedUser_AndSavesTimeZone()
    {
        var (data, service) = Create();

        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "android", InstallationId = "inst-1", TimeZone = "America/New_York" });

        var device = Assert.Single(data.Db.Set<UserDevice>());
        Assert.Equal(1, device.UserId);
        Assert.True(device.IsActive);
        Assert.Equal("America/New_York", data.Db.Set<UserPreference>().Single(p => p.UserId == 1).TimeZone);
    }

    [Fact]
    public async Task Register_SameTokenTwice_IsIdempotent()
    {
        var (data, service) = Create();

        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "ios" });
        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "ios" });

        Assert.Single(data.Db.Set<UserDevice>());
    }

    [Fact]
    public async Task Register_TokenOfAnotherAccount_MovesDeviceToNewUser()
    {
        var (data, service) = Create();
        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "ios" });

        await service.RegisterAsync(2, new RegisterDeviceRequest { Token = Token, Platform = "ios" });

        var device = Assert.Single(data.Db.Set<UserDevice>());
        Assert.Equal(2, device.UserId);
    }

    [Fact]
    public async Task Register_NewTokenForSameInstallation_DeactivatesOldToken()
    {
        var (data, service) = Create();
        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "ios", InstallationId = "inst" });

        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = "ExponentPushToken[zzzzzzzzzzzzzzzz]", Platform = "ios", InstallationId = "inst" });

        var old = data.Db.Set<UserDevice>().Single(d => d.DeviceToken == Token);
        Assert.False(old.IsActive);
        Assert.Equal(NotificationDeviceService.ReasonReplaced, old.DeactivationReason);
    }

    [Theory]
    [InlineData("not-a-token", "ios")]
    [InlineData(Token, "windows")]
    public async Task Register_InvalidTokenOrPlatform_IsRejected(string token, string platform)
    {
        var (_, service) = Create();

        await Assert.ThrowsAsync<AppValidationException>(() => service.RegisterAsync(1, new RegisterDeviceRequest { Token = token, Platform = platform }));
    }

    [Fact]
    public async Task Deactivate_OnlyAffectsTheCallersOwnToken()
    {
        var (data, service) = Create();
        await service.RegisterAsync(1, new RegisterDeviceRequest { Token = Token, Platform = "ios" });

        await service.DeactivateAsync(2, new DeactivateDeviceRequest { Token = Token });
        Assert.True(data.Db.Set<UserDevice>().Single().IsActive);

        await service.DeactivateAsync(1, new DeactivateDeviceRequest { Token = Token, Reason = "PermissionRevoked" });
        var device = data.Db.Set<UserDevice>().Single();
        Assert.False(device.IsActive);
        Assert.Equal("PermissionRevoked", device.DeactivationReason);
    }
}
