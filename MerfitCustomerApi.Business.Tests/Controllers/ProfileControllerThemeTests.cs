using System.Reflection;
using System.Security.Claims;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

public class ProfileControllerThemeTests
{
    private static ProfileController CreateController(Mock<IProfileService> profileService, long authenticatedUserId)
    {
        var controller = new ProfileController(profileService.Object);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, authenticatedUserId.ToString()) }, "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        return controller;
    }

    [Fact]
    public async Task GetThemePreference_ResolvesUserId_FromAuthenticatedClaimsPrincipal()
    {
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(s => s.GetThemePreferenceAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThemePreferenceResponse { ThemeMode = "System" });

        var controller = CreateController(profileService, authenticatedUserId: 7);

        var result = await controller.GetThemePreference(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ThemePreferenceResponse>(ok.Value);
        Assert.Equal("System", response.ThemeMode);
        profileService.Verify(s => s.GetThemePreferenceAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Guvenlik: hedef kullanici kimligi HER ZAMAN JWT'den (ClaimsPrincipal) gelir, istek govdesinden
    /// degil - UpdateThemePreferenceRequest'te zaten bir UserId alani yoktur. Iki farkli authenticated
    /// kullanici icin cagrinin yalnizca kendi id'siyle servise ulastigini dogrular.
    /// </summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task UpdateThemePreference_OnlyEverUpdatesTheAuthenticatedUsersOwnPreference(long authenticatedUserId)
    {
        var profileService = new Mock<IProfileService>();
        profileService
            .Setup(s => s.UpdateThemePreferenceAsync(authenticatedUserId, It.IsAny<UpdateThemePreferenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThemePreferenceResponse { ThemeMode = "Dark" });

        var controller = CreateController(profileService, authenticatedUserId);
        var request = new UpdateThemePreferenceRequest { ThemeMode = "Dark" };

        await controller.UpdateThemePreference(request, CancellationToken.None);

        profileService.Verify(
            s => s.UpdateThemePreferenceAsync(authenticatedUserId, request, It.IsAny<CancellationToken>()),
            Times.Once);
        profileService.Verify(
            s => s.UpdateThemePreferenceAsync(It.Is<long>(id => id != authenticatedUserId), It.IsAny<UpdateThemePreferenceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Unauthenticated istekler: [Authorize] class seviyesinde tanimli oldugu icin (degistirilmedi),
    /// tum action'lar (theme dahil) JWT dogrulanmadan 401 doner. Bu, ASP.NET Core pipeline'i
    /// tarafindan uygulandigi icin burada attribute'un varligini dogrulamak yeterlidir.
    /// </summary>
    [Fact]
    public void ProfileController_RequiresAuthorization_ForAllActions_IncludingTheme()
    {
        var authorizeAttribute = typeof(ProfileController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);

        var themeActions = new[]
        {
            nameof(ProfileController.GetThemePreference),
            nameof(ProfileController.UpdateThemePreference),
        };

        foreach (var actionName in themeActions)
        {
            var method = typeof(ProfileController).GetMethod(actionName);
            Assert.NotNull(method);
            Assert.Null(method!.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }
}
