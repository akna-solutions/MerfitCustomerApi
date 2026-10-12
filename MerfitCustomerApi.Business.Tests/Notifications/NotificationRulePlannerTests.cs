using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class NotificationRulePlannerTests
{
    // 14 Ekim 2026 Carsamba. Istanbul = UTC+3.
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static DateTime IstanbulToUtc(int hour, int minute = 0) => new DateTime(2026, 10, 14, hour, minute, 0, DateTimeKind.Utc).AddHours(-3);

    [Fact]
    public async Task NutritionRule_CreatesDispatchOnlyForUsersWhoHaveNotLoggedThePlannedMeal()
    {
        var data = new NotificationTestData();
        data.AddUser(1, firstName: "Ali");
        data.AddUser(2);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Lunch);
        data.AddNutritionPlan(2, DayOfWeek.Wednesday, MealType.Lunch);
        data.LogMeal(2, Wednesday, MealType.Lunch);
        data.AddRule(1, NotificationRuleKind.NutritionReminder, new TimeOnly(13, 0), MealType.Lunch);

        var created = await data.Planner.PlanDueRulesAsync(IstanbulToUtc(13, 5));

        Assert.Equal(1, created);
        var dispatch = Assert.Single(data.Db.Set<NotificationDispatch>());
        Assert.Equal(1, dispatch.UserId);
        Assert.Equal(NotificationType.NutritionReminder, dispatch.Type);
        Assert.Equal("Merhaba Ali", dispatch.Title);
        Assert.StartsWith("öğle yemeği kaydını", dispatch.Body);
        Assert.Equal(Wednesday, dispatch.LocalDate);
        Assert.Equal("rule:1:1:2026-10-14", dispatch.DedupeKey);
        Assert.Contains("\"screen\":\"nutrition\"", dispatch.DataJson);
    }

    [Fact]
    public async Task Rule_IsPlannedOnlyBetweenItsLocalTimeAndTheSendWindow()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0));

        Assert.Equal(0, await data.Planner.PlanDueRulesAsync(IstanbulToUtc(8, 59)));
        Assert.Equal(0, await data.Planner.PlanDueRulesAsync(IstanbulToUtc(11, 0)));
        Assert.Equal(1, await data.Planner.PlanDueRulesAsync(IstanbulToUtc(9, 1)));
    }

    [Fact]
    public async Task Rule_UsesEachUsersOwnTimeZone_AndDefaultWhenMissing()
    {
        var data = new NotificationTestData();
        data.AddUser(1, timeZone: "Europe/Istanbul");
        data.AddUser(2, timeZone: "America/New_York");
        data.AddUser(3, timeZone: null); // varsayilan Europe/Istanbul
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0));

        // 06:30Z = Istanbul 09:30, New York 02:30 (EDT).
        await data.Planner.PlanDueRulesAsync(new DateTime(2026, 10, 14, 6, 30, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { 1L, 3L }, data.Db.Set<NotificationDispatch>().Select(d => d.UserId).OrderBy(x => x));

        // 13:30Z = New York 09:30.
        await data.Planner.PlanDueRulesAsync(new DateTime(2026, 10, 14, 13, 30, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { 1L, 2L, 3L }, data.Db.Set<NotificationDispatch>().Select(d => d.UserId).OrderBy(x => x));
    }

    [Fact]
    public async Task RunningPlannerTwice_DoesNotCreateDuplicateDispatches()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday);
        data.AddRule(1, NotificationRuleKind.WorkoutReminder, new TimeOnly(18, 0));

        var first = await data.Planner.PlanDueRulesAsync(IstanbulToUtc(18, 1));
        var second = await data.Planner.PlanDueRulesAsync(IstanbulToUtc(18, 30));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(data.Db.Set<NotificationDispatch>());
    }

    [Fact]
    public async Task WorkoutRule_SkipsRestDaysAndCompletedWorkouts()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddUser(2);
        data.AddUser(3);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday, "Göğüs");
        data.AddWorkoutPlan(2, DayOfWeek.Wednesday);
        data.CompleteWorkout(2, IstanbulToUtc(7, 0));
        data.AddWorkoutPlan(3, DayOfWeek.Thursday); // Carsamba dinlenme gunu
        data.AddRule(1, NotificationRuleKind.WorkoutReminder, new TimeOnly(18, 0));

        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(18, 1));

        var dispatch = Assert.Single(data.Db.Set<NotificationDispatch>());
        Assert.Equal(1, dispatch.UserId);
        Assert.Contains("Göğüs", dispatch.Body);
    }

    [Fact]
    public async Task Rule_SkipsUsersWhoDisabledTheTypeOrHaveNoDevice()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddUser(2);
        data.AddUser(3, withDevice: false);
        data.Db.Set<UserNotificationTypeSetting>().Add(new UserNotificationTypeSetting { Id = 1, UserId = 2, Type = NotificationType.DailyMotivation, IsEnabled = false });
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0));

        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(9, 5));

        Assert.Equal(1, Assert.Single(data.Db.Set<NotificationDispatch>()).UserId);
    }

    [Fact]
    public async Task Rule_RespectsDaysOfWeekMask()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0), mask: 1 << (int)DayOfWeek.Monday);

        Assert.Equal(0, await data.Planner.PlanDueRulesAsync(IstanbulToUtc(9, 5)));
    }

    [Fact]
    public async Task MotivationRule_DoesNotRepeatYesterdaysTemplate()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0), templateCount: 3);
        var templates = data.Db.Set<NotificationTemplate>().OrderBy(t => t.Id).ToList();
        var todaysDefault = NotificationRulePlanner.PickTemplate(templates, 1, Wednesday, null);
        data.Db.Set<NotificationDispatch>().Add(new NotificationDispatch
        {
            Id = 99, UserId = 1, NotificationRuleId = 1, NotificationTemplateId = todaysDefault.Id,
            LocalDate = Wednesday.AddDays(-1), DedupeKey = "rule:1:1:2026-10-13", Status = NotificationDispatchStatus.Sent,
        });

        await data.Planner.PlanDueRulesAsync(IstanbulToUtc(9, 5));

        var today = data.Db.Set<NotificationDispatch>().Single(d => d.LocalDate == Wednesday);
        Assert.NotEqual(todaysDefault.Id, today.NotificationTemplateId);
    }

    [Fact]
    public async Task PausedRule_IsNotPlanned()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddRule(1, NotificationRuleKind.DailyMotivation, new TimeOnly(9, 0)).Status = NotificationRuleStatus.Paused;

        Assert.Equal(0, await data.Planner.PlanDueRulesAsync(IstanbulToUtc(9, 5)));
    }
}
