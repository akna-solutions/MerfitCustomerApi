using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Services.Notifications;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

public class NotificationServiceTests
{
    private const long UserId = 5;

    private sealed class Fixture
    {
        public required NotificationService Service { get; init; }
        public List<Notification> Notifications { get; } = new();
        public List<UserNotificationSetting> LegacySettings { get; } = new();
        public List<UserNotificationTypeSetting> TypeSettings { get; } = new();
        public required Mock<IUnitOfWork> UnitOfWork { get; init; }
    }

    private static Fixture CreateFixture()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var fixture = new Fixture { Service = new NotificationService(unitOfWork.Object), UnitOfWork = unitOfWork };
        unitOfWork.Setup(u => u.Repository<Notification>()).Returns(MockRepositoryFactory.Create(fixture.Notifications).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationSetting>()).Returns(MockRepositoryFactory.Create(fixture.LegacySettings).Object);
        unitOfWork.Setup(u => u.Repository<UserNotificationTypeSetting>()).Returns(MockRepositoryFactory.Create(fixture.TypeSettings).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return fixture;
    }

    private static Notification Make(long id, NotificationType type, DateTime createdAt, long userId = UserId, bool isRead = false, DateTime? expiresAt = null) => new()
    {
        Id = id,
        UserId = userId,
        Type = type,
        Title = $"n{id}",
        Body = "body",
        IsRead = isRead,
        CreatedAt = createdAt,
        ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task GetSettingsAsync_ListsEveryNotificationType()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.GetSettingsAsync(UserId);

        Assert.Equal(
            Enum.GetNames<NotificationType>().OrderBy(n => n),
            result.Select(r => r.Type).OrderBy(n => n));
        Assert.False(result.Single(r => r.Type == "Marketing").IsEnabled);
        Assert.True(result.Single(r => r.Type == "System").IsLocked);
        Assert.Equal("nutrition", result.Single(r => r.Type == "NutritionReminder").Category);
    }

    [Fact]
    public async Task UpdateSettingsAsync_LegacyType_CreatesRowWithDefaults_AndOnlyChangesRequestedColumn()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateSettingsAsync(UserId, new UpdateNotificationTypeSettingsRequest
        {
            Settings = { new UpdateNotificationTypeSettingItem { Type = "WorkoutReminder", IsEnabled = false } },
        });

        var row = Assert.Single(fixture.LegacySettings);
        Assert.False(row.WorkoutReminders);
        Assert.True(row.DailyGoalReminder);
        Assert.True(row.StreakReminder);
        Assert.False(row.ProductUpdates);
        Assert.False(result.Single(r => r.Type == "WorkoutReminder").IsEnabled);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NewType_IsStoredInTypeTable_AndUpdatedInPlace()
    {
        var fixture = CreateFixture();

        await fixture.Service.UpdateSettingsAsync(UserId, new UpdateNotificationTypeSettingsRequest
        {
            Settings = { new UpdateNotificationTypeSettingItem { Type = "nutritionReminder", IsEnabled = false } },
        });
        var result = await fixture.Service.UpdateSettingsAsync(UserId, new UpdateNotificationTypeSettingsRequest
        {
            Settings = { new UpdateNotificationTypeSettingItem { Type = "NutritionReminder", IsEnabled = true } },
        });

        var row = Assert.Single(fixture.TypeSettings);
        Assert.Equal(NotificationType.NutritionReminder, row.Type);
        Assert.True(row.IsEnabled);
        Assert.Empty(fixture.LegacySettings);
        Assert.True(result.Single(r => r.Type == "NutritionReminder").IsEnabled);
    }

    [Fact]
    public async Task UpdateSettingsAsync_LockedSystemType_CannotBeDisabled()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateSettingsAsync(UserId, new UpdateNotificationTypeSettingsRequest
        {
            Settings = { new UpdateNotificationTypeSettingItem { Type = "System", IsEnabled = false } },
        });

        Assert.Empty(fixture.TypeSettings);
        Assert.True(result.Single(r => r.Type == "System").IsEnabled);
    }

    [Fact]
    public async Task UpdateSettingsAsync_UnknownType_Throws()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<AppValidationException>(() => fixture.Service.UpdateSettingsAsync(UserId, new UpdateNotificationTypeSettingsRequest
        {
            Settings = { new UpdateNotificationTypeSettingItem { Type = "Nope", IsEnabled = false } },
        }));
    }

    [Fact]
    public async Task GetNotificationsAsync_NewestFirst_SkipsOtherUsersExpiredAndDisabledTypes()
    {
        var fixture = CreateFixture();
        var now = DateTime.UtcNow;
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, now.AddDays(-2)));
        fixture.Notifications.Add(Make(2, NotificationType.NutritionReminder, now.AddHours(-1)));
        fixture.Notifications.Add(Make(3, NotificationType.WorkoutReminder, now, userId: 99));
        fixture.Notifications.Add(Make(4, NotificationType.Achievement, now.AddDays(-1), expiresAt: now.AddMinutes(-5)));
        fixture.Notifications.Add(Make(5, NotificationType.Marketing, now)); // varsayilan olarak kapali

        var result = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest());

        Assert.Equal(new long[] { 2, 1 }, result.Items.Select(i => i.Id));
        Assert.Equal("nutrition", result.Items[0].Category);
    }

    [Fact]
    public async Task GetNotificationsAsync_NutritionCategory_ReturnsOnlyNutritionTypes()
    {
        var fixture = CreateFixture();
        var now = DateTime.UtcNow;
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, now));
        fixture.Notifications.Add(Make(2, NotificationType.NutritionReminder, now));
        fixture.Notifications.Add(Make(3, NotificationType.WaterReminder, now.AddMinutes(-1)));

        var result = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { Category = "nutrition" });

        Assert.Equal(new long[] { 2, 3 }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task UnreadCount_AndMarkAllAsRead_RespectCategory()
    {
        var fixture = CreateFixture();
        var now = DateTime.UtcNow;
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, now));
        fixture.Notifications.Add(Make(2, NotificationType.NutritionReminder, now));

        Assert.Equal(2, (await fixture.Service.GetUnreadCountAsync(UserId, null)).Count);

        await fixture.Service.MarkAllAsReadAsync(UserId, "nutrition");

        Assert.True(fixture.Notifications.Single(n => n.Id == 2).IsRead);
        Assert.False(fixture.Notifications.Single(n => n.Id == 1).IsRead);
        Assert.Equal(1, (await fixture.Service.GetUnreadCountAsync(UserId, null)).Count);
    }

    [Fact]
    public async Task MbFitAndMbShopInboxes_AreSeparate()
    {
        var fixture = CreateFixture();
        var now = DateTime.UtcNow;
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, now));
        fixture.Notifications.Add(Make(2, NotificationType.StoreOrderUpdate, now.AddMinutes(-1)));
        fixture.Notifications.Add(Make(3, NotificationType.Announcement, now.AddMinutes(-2)));

        var fit = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest());
        var fitExplicit = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { App = "mbfit" });
        var shop = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { App = "mbshop" });
        var shopByCategory = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { Category = "shop" });

        Assert.Equal(new long[] { 1, 3 }, fit.Items.Select(i => i.Id));
        Assert.Equal(new long[] { 1, 3 }, fitExplicit.Items.Select(i => i.Id));
        Assert.All(fit.Items, i => Assert.Equal("mbfit", i.App));
        Assert.Equal(new long[] { 2 }, shop.Items.Select(i => i.Id));
        Assert.Equal("mbshop", shop.Items[0].App);
        Assert.Equal("shop", shop.Items[0].Category);
        Assert.Equal(new long[] { 2 }, shopByCategory.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task UnreadCount_AndMarkAllAsRead_AreScopedToApp()
    {
        var fixture = CreateFixture();
        var now = DateTime.UtcNow;
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, now));
        fixture.Notifications.Add(Make(2, NotificationType.StoreOrderUpdate, now));

        Assert.Equal(1, (await fixture.Service.GetUnreadCountAsync(UserId, null, null)).Count);
        Assert.Equal(1, (await fixture.Service.GetUnreadCountAsync(UserId, "mbshop", null)).Count);

        await fixture.Service.MarkAllAsReadAsync(UserId, "mbshop", null);

        Assert.True(fixture.Notifications.Single(n => n.Id == 2).IsRead);
        Assert.False(fixture.Notifications.Single(n => n.Id == 1).IsRead);
    }

    [Fact]
    public async Task ShopSettings_AreGroupedSeparately_AndPromotionIsOffByDefault()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.GetSettingsAsync(UserId);

        var shop = result.Where(r => r.App == "mbshop").ToList();
        Assert.Equal(new[] { "StoreOrderUpdate", "StorePromotion" }, shop.Select(r => r.Type));
        Assert.All(shop, r => Assert.Equal("shop", r.Category));
        Assert.True(shop[0].IsEnabled);
        Assert.False(shop[1].IsEnabled);
        Assert.DoesNotContain(result.Where(r => r.App == "mbfit"), r => r.Category == "shop");
    }

    [Fact]
    public async Task MismatchedAppOrUnknownApp_IsRejected()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<AppValidationException>(() => fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { App = "nope" }));
        var empty = await fixture.Service.GetNotificationsAsync(UserId, new CustomerNotificationListRequest { App = "mbshop", Category = "nutrition" });
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task MarkAsReadAsync_OtherUsersNotification_IsNotFound()
    {
        var fixture = CreateFixture();
        fixture.Notifications.Add(Make(1, NotificationType.WorkoutReminder, DateTime.UtcNow, userId: 99));

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.MarkAsReadAsync(UserId, 1));
        Assert.False(fixture.Notifications[0].IsRead);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_SavesPushSwitchAndQuietHours()
    {
        var fixture = CreateFixture();
        var preferences = new List<UserPreference>();
        fixture.UnitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(preferences).Object);

        var result = await fixture.Service.UpdatePreferencesAsync(UserId, new UpdateNotificationPreferencesRequest
        {
            PushEnabled = false,
            QuietHoursStart = "22:30",
            QuietHoursEnd = "07:00",
            TimeZone = "Europe/Berlin",
        });

        var row = Assert.Single(fixture.LegacySettings);
        Assert.False(row.PushEnabled);
        Assert.Equal(new TimeOnly(22, 30), row.QuietHoursStart);
        Assert.Equal("Europe/Berlin", Assert.Single(preferences).TimeZone);
        Assert.False(result.PushEnabled);
        Assert.Equal("Europe/Berlin", result.EffectiveTimeZone);
    }

    [Theory]
    [InlineData("22:00", null, null)]
    [InlineData("25:00", "07:00", null)]
    [InlineData(null, null, "Mars/Olympus")]
    public async Task UpdatePreferencesAsync_InvalidInput_Throws(string? start, string? end, string? timeZone)
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.Setup(u => u.Repository<UserPreference>()).Returns(MockRepositoryFactory.Create(new List<UserPreference>()).Object);

        await Assert.ThrowsAsync<AppValidationException>(() => fixture.Service.UpdatePreferencesAsync(UserId, new UpdateNotificationPreferencesRequest
        {
            QuietHoursStart = start,
            QuietHoursEnd = end,
            TimeZone = timeZone,
        }));
    }
}
