using Xunit;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Tests.Personalization;

public class TrainingDayPlannerTests
{
    private static List<DayOfWeek> Days(params string[] codes) => TrainingDayCodes.ParseStored(string.Join(",", codes));
    private static string[] Codes(IEnumerable<DayOfWeek> days) => days.Select(TrainingDayCodes.ToCode).ToArray();

    [Theory]
    [InlineData(FitnessGoal.BuildMuscle, ExperienceLevel.Beginner, 3)]
    [InlineData(FitnessGoal.BuildMuscle, ExperienceLevel.Intermediate, 4)]
    [InlineData(FitnessGoal.GetStronger, ExperienceLevel.Advanced, 5)]
    [InlineData(FitnessGoal.LoseWeight, ExperienceLevel.Beginner, 4)]
    [InlineData(FitnessGoal.LoseWeight, ExperienceLevel.Advanced, 6)]
    [InlineData(FitnessGoal.ImproveFitness, ExperienceLevel.Beginner, 3)]
    public void TargetTrainingDays_FollowsGoalAndExperience(FitnessGoal goal, ExperienceLevel experience, int expected)
    {
        Assert.Equal(expected, TrainingDayPlanner.TargetTrainingDays(goal, experience));
    }

    [Theory]
    [InlineData(2, "Mon,Thu")]
    [InlineData(3, "Mon,Wed,Fri")]
    [InlineData(4, "Mon,Tue,Thu,Sat")]
    [InlineData(5, "Mon,Tue,Wed,Fri,Sat")]
    [InlineData(6, "Mon,Tue,Wed,Thu,Fri,Sat")]
    public void WholeWeek_SpreadsRestDaysEvenly(int target, string expected)
    {
        Assert.Equal(expected.Split(','), Codes(TrainingDayPlanner.ChooseDays(Array.Empty<DayOfWeek>(), target)));
    }

    [Fact]
    public void AvailableDays_AreRespected_AndConsecutiveDaysAvoidedWhenPossible()
    {
        var chosen = Codes(TrainingDayPlanner.ChooseDays(Days("Mon", "Tue", "Wed", "Thu", "Sat"), 3));

        Assert.Equal(new[] { "Mon", "Wed", "Sat" }, chosen);
    }

    [Fact]
    public void FewerAvailableDaysThanTarget_UsesAllAvailableDays()
    {
        Assert.Equal(new[] { "Tue", "Fri" }, Codes(TrainingDayPlanner.ChooseDays(Days("Fri", "Tue"), 4)));
    }

    [Fact]
    public void WeekendOnly_UsesBothDays()
    {
        Assert.Equal(new[] { "Sat", "Sun" }, Codes(TrainingDayPlanner.ChooseDays(Days("Sat", "Sun"), 3)));
    }

    [Theory]
    [InlineData(new[] { "Mon" }, false)]
    [InlineData(new[] { "Mon", "Funday" }, false)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "mon", "FRI" }, true)]
    public void AvailableDays_Validation(string[] codes, bool valid)
    {
        Assert.Equal(valid, TrainingSchedule.TryParseAvailableDays(codes, out _, out _));
    }
}
