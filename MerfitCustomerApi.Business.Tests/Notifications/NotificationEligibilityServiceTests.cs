using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class NotificationEligibilityServiceTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    [Fact]
    public async Task MissingMeal_PlannedButNotLogged_IsReturned_LoggedIsNot()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddUser(2);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Lunch);
        data.AddNutritionPlan(2, DayOfWeek.Wednesday, MealType.Lunch);
        data.LogMeal(2, Wednesday, MealType.Lunch);

        var missing = await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1, 2 }, Wednesday, MealType.Lunch, onlyUsersWithPlan: true);

        Assert.Equal(new[] { 1L }, missing.OrderBy(x => x));
    }

    [Fact]
    public async Task MissingMeal_MealRowWithoutItems_CountsAsNotLogged()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Dinner);
        data.LogMeal(1, Wednesday, MealType.Dinner, withItem: false);

        var missing = await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1 }, Wednesday, MealType.Dinner, true);

        Assert.Contains(1L, missing);
    }

    [Fact]
    public async Task MissingMeal_MealLoggedForAnotherDayOrType_DoesNotCount()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Lunch);
        data.LogMeal(1, Wednesday.AddDays(-1), MealType.Lunch);
        data.LogMeal(1, Wednesday, MealType.Breakfast);

        var missing = await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1 }, Wednesday, MealType.Lunch, true);

        Assert.Contains(1L, missing);
    }

    [Fact]
    public async Task MissingMeal_PlanWithoutThatMeal_IsNotExpected()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddNutritionPlan(1, DayOfWeek.Wednesday, MealType.Breakfast);

        var missing = await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1 }, Wednesday, MealType.Snack, onlyUsersWithPlan: false);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task MissingMeal_UserWithoutPlan_OnlyExpectedWhenRuleAllows()
    {
        var data = new NotificationTestData();
        data.AddUser(1);

        Assert.Empty(await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1 }, Wednesday, MealType.Lunch, onlyUsersWithPlan: true));
        Assert.Contains(1L, await data.Eligibility.GetUsersMissingMealAsync(new long[] { 1 }, Wednesday, MealType.Lunch, onlyUsersWithPlan: false));
    }

    [Fact]
    public async Task IncompleteWorkout_ScheduledAndNotCompleted_IsReturnedWithWorkoutName()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday, "Bacak Günü");
        var (start, end) = (new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));

        var result = await data.Eligibility.GetUsersWithIncompleteWorkoutAsync(new long[] { 1 }, Wednesday, start, end);

        Assert.Equal("Bacak Günü", result[1].WorkoutName);
    }

    [Fact]
    public async Task IncompleteWorkout_CompletedWithinLocalDay_IsExcluded()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday);
        // Yerel gun 14 Ekim (UTC+3) = [13 Ekim 21:00Z, 14 Ekim 21:00Z). UTC tarihi 13 Ekim olsa da yerel gunde.
        data.CompleteWorkout(1, new DateTime(2026, 10, 13, 22, 30, 0, DateTimeKind.Utc));

        var result = await data.Eligibility.GetUsersWithIncompleteWorkoutAsync(new long[] { 1 }, Wednesday,
            new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));

        Assert.Empty(result);
    }

    [Fact]
    public async Task IncompleteWorkout_AbandonedOrPreviousDaySession_DoesNotCountAsCompleted()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Wednesday);
        data.CompleteWorkout(1, new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc), WorkoutSessionStatus.Abandoned);
        data.CompleteWorkout(1, new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc));

        var result = await data.Eligibility.GetUsersWithIncompleteWorkoutAsync(new long[] { 1 }, Wednesday,
            new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));

        Assert.True(result.ContainsKey(1));
    }

    [Fact]
    public async Task IncompleteWorkout_RestDay_IsNotScheduled()
    {
        var data = new NotificationTestData();
        data.AddUser(1);
        data.AddWorkoutPlan(1, DayOfWeek.Monday);

        var result = await data.Eligibility.GetUsersWithIncompleteWorkoutAsync(new long[] { 1 }, Wednesday,
            new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));

        Assert.Empty(result);
    }
}
