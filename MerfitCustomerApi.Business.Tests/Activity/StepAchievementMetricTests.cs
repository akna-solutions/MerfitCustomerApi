using MerfitCustomerApi.Business.Services.Scoring;
using Xunit;
using StepDay = MerfitCustomerApi.Business.Services.Scoring.MerfitScoreService.StepDay;

namespace MerfitCustomerApi.Business.Tests.Activity;

/// <summary>Adim basari kosullarinin (steps_*) metrik hesabi.</summary>
public class StepAchievementMetricTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void TotalAndDailyMax()
    {
        var days = new[] { new StepDay(Monday, 800, 10_000), new StepDay(Monday.AddDays(1), 21_000, 10_000) };

        Assert.Equal(21_800, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepsTotalCondition, days, 5_000));
        Assert.Equal(21_000, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepsDailyMaxCondition, days, 5_000));
    }

    [Fact]
    public void WeeklyMax_UsesMondayToSundayWeeks()
    {
        // Pazar (22 Kasim) 30.000 + sonraki Pazartesi 30.000: farkli haftalar, ikisi de 50.000'e ulasmaz.
        var days = new[]
        {
            new StepDay(Monday, 10_000, 10_000),
            new StepDay(Monday.AddDays(6), 30_000, 10_000),
            new StepDay(Monday.AddDays(7), 30_000, 10_000),
        };

        Assert.Equal(40_000, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepsWeeklyMaxCondition, days, 5_000));
    }

    [Fact]
    public void GoalStreak_CountsConsecutiveDaysAndAppliesMinimumGoal()
    {
        var days = new List<StepDay>();
        for (var i = 0; i < 7; i++)
        {
            days.Add(new StepDay(Monday.AddDays(i), 8_000, 7_500));
        }

        Assert.Equal(7, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepGoalStreakDaysCondition, days, 5_000));

        // Bir gun eksik (bosluk) seriyi boler.
        days.RemoveAt(3);
        Assert.Equal(3, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepGoalStreakDaysCondition, days, 5_000));

        // Hedefini 1.000'e indiren kullanici 2.000 adimla seri kazanamaz (en dusuk 5.000).
        var lowGoal = Enumerable.Range(0, 7).Select(i => new StepDay(Monday.AddDays(i), 2_000, 1_000)).ToList();
        Assert.Equal(0, MerfitScoreService.ComputeStepMetric(MerfitScoreService.StepGoalStreakDaysCondition, lowGoal, 5_000));
    }
}
