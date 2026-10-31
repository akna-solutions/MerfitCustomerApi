using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class NotificationDispatchProcessorTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static DateTime IstanbulToUtc(int hour, int minute = 0) => new DateTime(2026, 10, 14, hour, minute, 0, DateTimeKind.Utc).AddHours(-3);

    [Fact]
    public async Task EligibleDispatch_CreatesInboxNotification_PushesToActiveDevices_AndIsSent()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddDevice(11, 1);
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        Assert.Equal(1, await data.Processor.ProcessDueAsync(now));

        Assert.Equal(NotificationDispatchStatus.Sent, dispatch.Status);
        var inbox = Assert.Single(data.Db.Set<Notification>());
        Assert.Equal(inbox.Id, dispatch.NotificationId);
        Assert.Equal(2, data.Push.Sent.Count);
        Assert.All(data.Push.Sent, m => Assert.Equal(inbox.Id.ToString(), m.Data["notificationId"]));
        Assert.All(data.Db.Set<NotificationDeliveryAttempt>(), a => Assert.Equal(NotificationDeliveryStatus.Accepted, a.Status));
        Assert.Null(dispatch.LockToken);
    }

    [Fact]
    public async Task NutritionReminder_MealLoggedAfterPlanning_IsCancelledWithoutPushOrInbox()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Lunch);
        data.AddRule(1, NotificationRuleKind.NutritionReminder, new TimeOnly(13, 0), MealType.Lunch);
        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(13, 1));
        data.LogMeal(1, Wednesday, MealType.Lunch);

        await data.Processor.ProcessDueAsync(IstanbulToUtc(13, 2));

        var dispatch = Assert.Single(data.Db.Set<NotificationDispatch>());
        Assert.Equal(NotificationDispatchStatus.Cancelled, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonConditionMet, dispatch.StatusReason);
        Assert.Empty(data.Push.Sent);
        Assert.Empty(data.Db.Set<Notification>());
    }

    [Fact]
    public async Task NutritionReminder_MealStillNotLogged_IsSent()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Lunch);
        data.AddRule(1, NotificationRuleKind.NutritionReminder, new TimeOnly(13, 0), MealType.Lunch);
        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(13, 1));

        await data.Processor.ProcessDueAsync(IstanbulToUtc(13, 2));

        Assert.Equal(NotificationDispatchStatus.Sent, Assert.Single(data.Db.Set<NotificationDispatch>()).Status);
        Assert.Single(data.Push.Sent);
    }

    [Fact]
    public async Task WorkoutReminder_WorkoutCompletedBeforeDispatch_IsCancelled()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday);
        data.AddRule(1, NotificationRuleKind.WorkoutReminder, new TimeOnly(18, 0));
        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(18, 1));
        data.CompleteWorkout(1, IstanbulToUtc(18, 1));

        await data.Processor.ProcessDueAsync(IstanbulToUtc(18, 2));

        Assert.Equal(NotificationDispatchStatus.Cancelled, Assert.Single(data.Db.Set<NotificationDispatch>()).Status);
        Assert.Empty(data.Push.Sent);
    }

    [Fact]
    public async Task ReminderCanceller_CompletedWorkout_CancelsPendingReminderImmediately()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday);
        data.AddRule(1, NotificationRuleKind.WorkoutReminder, new TimeOnly(18, 0));
        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(18, 1));

        await new NotificationReminderCanceller(data.Db.Object).CancelWorkoutRemindersAsync(1, IstanbulToUtc(18, 1));

        var dispatch = Assert.Single(data.Db.Set<NotificationDispatch>());
        Assert.Equal(NotificationDispatchStatus.Cancelled, dispatch.Status);
        Assert.Equal(0, await data.Processor.ProcessDueAsync(IstanbulToUtc(18, 2)));
    }

    [Fact]
    public async Task DisabledPreference_IsSuppressed()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.Db.Set<UserNotificationTypeSetting>().Add(new UserNotificationTypeSetting { Id = 1, UserId = 1, Type = NotificationType.Announcement, IsEnabled = false });
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonPreferenceDisabled, dispatch.StatusReason);
        Assert.Empty(data.Db.Set<Notification>());
        Assert.Empty(data.Push.Sent);
    }

    [Fact]
    public async Task GlobalPushDisabled_WritesInboxOnly()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.Db.Set<UserNotificationSetting>().Add(new UserNotificationSetting { Id = 1, UserId = 1, PushEnabled = false, WorkoutReminders = true });
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.InAppOnly, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonPushDisabled, dispatch.StatusReason);
        Assert.Single(data.Db.Set<Notification>());
        Assert.Empty(data.Push.Sent);
    }

    [Fact]
    public async Task QuietHours_ReminderIsSuppressed_AnnouncementIsDeferredToQuietHoursEnd()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.Db.Set<UserNotificationSetting>().Add(new UserNotificationSetting { Id = 1, UserId = 1, PushEnabled = true, QuietHoursStart = new TimeOnly(22, 0), QuietHoursEnd = new TimeOnly(7, 0) });
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(22, 30));
        var now = IstanbulToUtc(22, 45);
        var reminder = data.AddDispatch(1, 1, NotificationType.DailyMotivation, NotificationDispatchSource.Rule, ruleId: 1, localDate: Wednesday, now: now);
        var announcement = data.AddDispatch(2, 1, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Suppressed, reminder.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonQuietHours, reminder.StatusReason);
        Assert.Equal(NotificationDispatchStatus.Pending, announcement.Status);
        Assert.Equal(new DateTime(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc), announcement.NextAttemptAtUtc);
        Assert.Equal(0, announcement.AttemptCount);
        Assert.Empty(data.Push.Sent);
        Assert.Empty(data.Db.Set<Notification>());
    }

    [Fact]
    public async Task DailyLimit_ExceededForNonExemptType_WritesInboxOnly_RewardIsExempt()
    {
        var data = new NotificationTestData();
        data.Options.DailyPushLimit = 1;
        data.AddUser(1);
        var now = IstanbulToUtc(15);
        data.Db.Set<NotificationDispatch>().Add(new NotificationDispatch
        {
            Id = 50, UserId = 1, Type = NotificationType.DailyMotivation, Status = NotificationDispatchStatus.Sent,
            ProcessedAtUtc = IstanbulToUtc(9), DedupeKey = "old", NextAttemptAtUtc = IstanbulToUtc(9), ScheduledForUtc = IstanbulToUtc(9),
        });
        var announcement = data.AddDispatch(1, 1, now: now);
        var reward = data.AddDispatch(2, 1, NotificationType.Reward, NotificationDispatchSource.MonthlyReward, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.InAppOnly, announcement.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonDailyLimit, announcement.StatusReason);
        Assert.Equal(NotificationDispatchStatus.Sent, reward.Status);
    }

    [Fact]
    public async Task DailyLimit_IsCountedPerApp_ShopPushDoesNotConsumeMbFitLimit()
    {
        var data = new NotificationTestData();
        data.Options.DailyPushLimit = 1;
        data.Options.ShopDailyPushLimit = 1;
        data.AddUser(1);
        var now = IstanbulToUtc(15);
        // Bugun bir MB Shop kampanya push'u gitti: MB Fit limiti etkilenmez, MB Shop limiti doldu.
        data.Db.Set<NotificationDispatch>().Add(new NotificationDispatch
        {
            Id = 50, UserId = 1, Type = NotificationType.StorePromotion, Status = NotificationDispatchStatus.Sent,
            ProcessedAtUtc = IstanbulToUtc(9), DedupeKey = "old", NextAttemptAtUtc = IstanbulToUtc(9), ScheduledForUtc = IstanbulToUtc(9),
        });
        data.Db.Set<UserNotificationTypeSetting>().Add(new UserNotificationTypeSetting { UserId = 1, Type = NotificationType.StorePromotion, IsEnabled = true });
        var announcement = data.AddDispatch(1, 1, now: now);
        var promotion = data.AddDispatch(2, 1, NotificationType.StorePromotion, now: now);
        var order = data.AddDispatch(3, 1, NotificationType.StoreOrderUpdate, NotificationDispatchSource.StoreOrder, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Sent, announcement.Status);
        Assert.Equal(NotificationDispatchStatus.InAppOnly, promotion.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonDailyLimit, promotion.StatusReason);
        Assert.Equal(NotificationDispatchStatus.Sent, order.Status); // siparis durumu limitten muaf
    }

    [Fact]
    public async Task ShopNotification_UsesShopAndroidChannel_AndCarriesAppKey()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        var now = IstanbulToUtc(12);
        data.AddDispatch(1, 1, now: now);
        data.AddDispatch(2, 1, NotificationType.StoreOrderUpdate, NotificationDispatchSource.StoreOrder, now: now);

        await data.Processor.ProcessDueAsync(now);

        var fit = data.Push.Sent.Where(m => m.Data["type"] == "Announcement").ToList();
        var shop = data.Push.Sent.Where(m => m.Data["type"] == "StoreOrderUpdate").ToList();
        Assert.NotEmpty(fit);
        Assert.NotEmpty(shop);
        Assert.All(fit, m => { Assert.Null(m.ChannelId); Assert.Equal("mbfit", m.Data["app"]); });
        Assert.All(shop, m => { Assert.Equal("mbshop", m.ChannelId); Assert.Equal("mbshop", m.Data["app"]); });
    }

    [Fact]
    public async Task DeviceNotRegistered_DeactivatesDevice_AndFallsBackToInbox()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        var device = data.Db.Set<UserDevice>().Single();
        data.Push.ResultsByToken[device.DeviceToken] = new PushSendResult(PushSendOutcome.Rejected, ErrorCode: PushErrorCodes.DeviceNotRegistered);
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.False(device.IsActive);
        Assert.Equal("DeviceNotRegistered", device.DeactivationReason);
        Assert.Equal(NotificationDispatchStatus.InAppOnly, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonDevicesInvalid, dispatch.StatusReason);
        Assert.Equal(NotificationDeliveryStatus.Rejected, Assert.Single(data.Db.Set<NotificationDeliveryAttempt>()).Status);
    }

    [Fact]
    public async Task TransientError_IsRetriedWithBackoff_WithoutDuplicatingInboxOrResendingToAcceptedDevice()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        var flaky = data.AddDevice(11, 1);
        var good = data.Db.Set<UserDevice>().First(d => d.Id == 10);
        data.Push.ResultsByToken[flaky.DeviceToken] = new PushSendResult(PushSendOutcome.Transient, ErrorCode: "HTTP503");
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        // Ilk deneme: iyi cihaz kabul edildi, digeri gecici hata -> kabul edilen oldugu icin Sent.
        await data.Processor.ProcessDueAsync(now);
        Assert.Equal(NotificationDispatchStatus.Sent, dispatch.Status);

        // Yalnizca gecici hata alan tek cihazli kullanici: yeniden denenir.
        var data2 = new NotificationTestData();
        data2.AddUser(2);
        var only = data2.Db.Set<UserDevice>().Single();
        data2.Push.ResultsByToken[only.DeviceToken] = new PushSendResult(PushSendOutcome.Transient, ErrorCode: "HTTP503");
        var retried = data2.AddDispatch(1, 2, now: now);

        await data2.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Pending, retried.Status);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Equal(now + data2.Options.RetryBaseDelay, retried.NextAttemptAtUtc);
        Assert.Single(data2.Db.Set<Notification>());

        // Ikinci deneme saglayici kabul eder; bildirim kutusu tekrar olusmaz.
        data2.Push.ResultsByToken.Clear();
        await data2.Processor.ProcessDueAsync(retried.NextAttemptAtUtc);

        Assert.Equal(NotificationDispatchStatus.Sent, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Single(data2.Db.Set<Notification>());
        Assert.Equal(2, data2.Push.Sent.Count);
        _ = good;
    }

    [Fact]
    public async Task TransientError_StopsAfterMaxAttempts()
    {
        var data = new NotificationTestData();
        data.Options.MaxDeliveryAttempts = 2;
        data.AddUser(1);
        data.Push.ThrowOnSend = new HttpRequestException("network down");
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);
        Assert.Equal(NotificationDispatchStatus.Pending, dispatch.Status);
        await data.Processor.ProcessDueAsync(dispatch.NextAttemptAtUtc);

        Assert.Equal(NotificationDispatchStatus.Failed, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonRetriesExhausted, dispatch.StatusReason);
        Assert.Equal(2, dispatch.AttemptCount);
    }

    [Fact]
    public async Task ExpiredDispatch_IsSuppressed()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, expiresAtUtc: now.AddMinutes(-1), now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal(NotificationDispatchProcessor.ReasonExpired, dispatch.StatusReason);
    }

    [Fact]
    public async Task PausedRuleOrCancelledCampaign_CancelsPendingDispatches()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0)).Status = NotificationRuleStatus.Paused;
        data.Db.Set<NotificationCampaign>().Add(new NotificationCampaign { Id = 7, Status = NotificationCampaignStatus.Cancelled, Name = "c" });
        var now = IstanbulToUtc(9, 5);
        var fromRule = data.AddDispatch(1, 1, NotificationType.DailyMotivation, NotificationDispatchSource.Rule, ruleId: 1, localDate: Wednesday, now: now);
        var fromCampaign = data.AddDispatch(2, 1, campaignId: 7, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchStatus.Cancelled, fromRule.Status);
        Assert.Equal(NotificationDispatchStatus.Cancelled, fromCampaign.Status);
        Assert.Empty(data.Push.Sent);
    }

    [Fact]
    public async Task InactiveUser_IsSuppressed()
    {
        var data = new NotificationTestData();
        data.AddUser(1).IsActive = false;
        var now = IstanbulToUtc(12);
        var dispatch = data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);

        Assert.Equal(NotificationDispatchProcessor.ReasonUserInactive, dispatch.StatusReason);
    }

    [Fact]
    public async Task ProcessingTwice_DoesNotResendFinishedDispatch()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        var now = IstanbulToUtc(12);
        data.AddDispatch(1, 1, now: now);

        await data.Processor.ProcessDueAsync(now);
        var second = await data.Processor.ProcessDueAsync(now.AddMinutes(1));

        Assert.Equal(0, second);
        Assert.Single(data.Push.Sent);
        Assert.Single(data.Db.Set<Notification>());
    }

    [Fact]
    public async Task Receipts_DeviceNotRegistered_DeactivatesDevice_OkMarksReceipt()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddDevice(11, 1);
        var now = IstanbulToUtc(12);
        data.AddDispatch(1, 1, now: now);
        await data.Processor.ProcessDueAsync(now);
        var attempts = data.Db.Set<NotificationDeliveryAttempt>().OrderBy(a => a.UserDeviceId).ToList();
        foreach (var a in attempts)
        {
            a.CreatedAt = now;
        }
        data.Push.Receipts[attempts[0].ProviderMessageId!] = new PushReceiptResult(true);
        data.Push.Receipts[attempts[1].ProviderMessageId!] = new PushReceiptResult(false, PushErrorCodes.DeviceNotRegistered);

        var checkedCount = await data.Processor.CheckReceiptsAsync(now.AddMinutes(20));

        Assert.Equal(2, checkedCount);
        Assert.Equal(NotificationDeliveryStatus.ReceiptOk, attempts[0].Status);
        Assert.Equal(NotificationDeliveryStatus.ReceiptError, attempts[1].Status);
        Assert.False(data.Db.Set<UserDevice>().Single(d => d.Id == attempts[1].UserDeviceId).IsActive);
        Assert.True(data.Db.Set<UserDevice>().Single(d => d.Id == attempts[0].UserDeviceId).IsActive);
    }

    [Fact]
    public async Task CompleteFinishedCampaigns_OnlyWhenNoOpenDispatchRemains()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.Db.Set<NotificationCampaign>().Add(new NotificationCampaign { Id = 1, Status = NotificationCampaignStatus.Dispatching, Name = "a" });
        data.Db.Set<NotificationCampaign>().Add(new NotificationCampaign { Id = 2, Status = NotificationCampaignStatus.Dispatching, Name = "b" });
        var now = IstanbulToUtc(12);
        data.AddDispatch(1, 1, campaignId: 1, now: now).Status = NotificationDispatchStatus.Sent;
        data.AddDispatch(2, 1, campaignId: 2, now: now);

        Assert.Equal(1, await data.Processor.CompleteFinishedCampaignsAsync(now));
        Assert.Equal(NotificationCampaignStatus.Completed, data.Db.Set<NotificationCampaign>().Single(c => c.Id == 1).Status);
        Assert.Equal(NotificationCampaignStatus.Dispatching, data.Db.Set<NotificationCampaign>().Single(c => c.Id == 2).Status);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(20, 3600)]
    public void RetryDelay_IsExponentialAndBounded(int attempt, int expectedSeconds)
    {
        var options = new MerfitCustomerApi.Business.Common.NotificationOptions();
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), NotificationDispatchProcessor.RetryDelay(attempt, options));
    }
}
