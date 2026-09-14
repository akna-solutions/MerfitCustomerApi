using System.Linq.Expressions;
using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Services.Profile;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using MerfitCustomerApi.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

public class ProfileServiceThemeTests
{
    private static (ProfileService Service, List<UserPreference> Preferences) CreateService()
    {
        var preferences = new List<UserPreference>();

        var preferenceRepo = new Mock<IGenericRepository<UserPreference>>();

        preferenceRepo
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<UserPreference, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserPreference, bool>> predicate, CancellationToken _) =>
                preferences.AsQueryable().FirstOrDefault(predicate));

        preferenceRepo
            .Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(() => preferences.AsAsyncQueryable());

        preferenceRepo
            .Setup(r => r.AddAsync(It.IsAny<UserPreference>(), It.IsAny<CancellationToken>()))
            .Callback<UserPreference, CancellationToken>((preference, _) => preferences.Add(preference))
            .Returns(Task.CompletedTask);

        preferenceRepo.Setup(r => r.Update(It.IsAny<UserPreference>()));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(preferenceRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return (new ProfileService(unitOfWork.Object), preferences);
    }

    [Fact]
    public async Task GetThemePreferenceAsync_ReturnsStoredTheme_WhenPreferenceExists()
    {
        var (service, preferences) = CreateService();
        preferences.Add(new UserPreference { Id = 1, UserId = 42, Language = "tr", Theme = Theme.Dark });

        var result = await service.GetThemePreferenceAsync(42);

        Assert.Equal("Dark", result.ThemeMode);
    }

    [Fact]
    public async Task GetThemePreferenceAsync_DefaultsToSystem_WhenNoPreferenceExists()
    {
        var (service, _) = CreateService();

        var result = await service.GetThemePreferenceAsync(42);

        Assert.Equal("System", result.ThemeMode);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("dark")]
    [InlineData("SYSTEM")]
    public async Task UpdateThemePreferenceAsync_UpdatesExistingPreference_CaseInsensitive(string requestedTheme)
    {
        var (service, preferences) = CreateService();
        preferences.Add(new UserPreference { Id = 1, UserId = 42, Language = "tr", Theme = Theme.System });

        var result = await service.UpdateThemePreferenceAsync(42, new UpdateThemePreferenceRequest { ThemeMode = requestedTheme });

        Assert.Single(preferences);
        Assert.Equal(requestedTheme, result.ThemeMode, ignoreCase: true);
        Assert.Equal(Enum.Parse<Theme>(requestedTheme, ignoreCase: true), preferences[0].Theme);
    }

    [Fact]
    public async Task UpdateThemePreferenceAsync_CreatesPreference_WithRequestedTheme_WhenNoneExists()
    {
        var (service, preferences) = CreateService();

        var result = await service.UpdateThemePreferenceAsync(42, new UpdateThemePreferenceRequest { ThemeMode = "Dark" });

        var created = Assert.Single(preferences);
        Assert.Equal(42, created.UserId);
        Assert.Equal(Theme.Dark, created.Theme);
        Assert.Equal("Dark", result.ThemeMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Blue")]
    [InlineData("systemm")]
    public async Task UpdateThemePreferenceAsync_ThrowsAppValidationException_ForInvalidValue(string invalidTheme)
    {
        var (service, preferences) = CreateService();

        await Assert.ThrowsAsync<AppValidationException>(
            () => service.UpdateThemePreferenceAsync(42, new UpdateThemePreferenceRequest { ThemeMode = invalidTheme }));

        Assert.Empty(preferences);
    }

    [Fact]
    public async Task UpdateThemePreferenceAsync_OnlyAffectsTheRequestedUsersPreference()
    {
        var (service, preferences) = CreateService();
        preferences.Add(new UserPreference { Id = 1, UserId = 1, Language = "tr", Theme = Theme.Light });
        preferences.Add(new UserPreference { Id = 2, UserId = 2, Language = "tr", Theme = Theme.Light });

        await service.UpdateThemePreferenceAsync(2, new UpdateThemePreferenceRequest { ThemeMode = "Dark" });

        Assert.Equal(Theme.Light, preferences.Single(p => p.UserId == 1).Theme);
        Assert.Equal(Theme.Dark, preferences.Single(p => p.UserId == 2).Theme);
    }
}
