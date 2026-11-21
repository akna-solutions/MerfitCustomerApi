using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Activity;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Activity;

/// <summary>Profilden otomatik gunluk adim hedefi.</summary>
public class StepGoalCalculatorTests
{
    private static readonly StepActivityOptions Options = new();

    [Theory]
    [InlineData(ActivityLevel.Sedentary, FitnessGoal.BuildMuscle, 30, 6_000)]
    [InlineData(ActivityLevel.LightlyActive, FitnessGoal.GetStronger, 30, 7_500)]
    [InlineData(ActivityLevel.ModeratelyActive, FitnessGoal.BuildMuscle, 30, 9_000)]
    [InlineData(ActivityLevel.VeryActive, FitnessGoal.BuildMuscle, 30, 10_000)]
    [InlineData(ActivityLevel.ExtraActive, FitnessGoal.BuildMuscle, 30, 11_000)]
    [InlineData(ActivityLevel.Sedentary, FitnessGoal.LoseWeight, 30, 7_500)]
    [InlineData(ActivityLevel.ExtraActive, FitnessGoal.LoseWeight, 30, 12_500)]
    [InlineData(ActivityLevel.VeryActive, FitnessGoal.BuildMuscle, 62, 9_000)]
    [InlineData(ActivityLevel.ExtraActive, FitnessGoal.LoseWeight, 70, 8_000)]
    public void Calculate_FollowsProfile(ActivityLevel level, FitnessGoal goal, int age, int expected)
    {
        Assert.Equal(expected, StepGoalCalculator.Calculate(level, goal, age, Options).Goal);
    }

    [Fact]
    public void Calculate_WithoutActivityLevel_UsesDefault_AndExplainsHowToPersonalize()
    {
        var result = StepGoalCalculator.Calculate(null, null, null, Options);

        Assert.Equal(10_000, result.Goal);
        Assert.Contains("aktivite seviyeni", result.Reason);
    }

    [Fact]
    public void Calculate_ReasonNamesTheInputsUsed()
    {
        var result = StepGoalCalculator.Calculate(ActivityLevel.Sedentary, FitnessGoal.LoseWeight, 66, Options);

        Assert.Equal("Hedefin aktivite seviyene, kilo verme hedefine ve yaşına göre otomatik belirlendi.", result.Reason);
    }

    [Fact]
    public void Calculate_StaysWithinConfiguredBounds()
    {
        var options = new StepActivityOptions { AutoGoalMin = 7_000, AutoGoalMax = 9_000 };

        Assert.Equal(7_000, StepGoalCalculator.Calculate(ActivityLevel.Sedentary, null, 30, options).Goal);
        Assert.Equal(9_000, StepGoalCalculator.Calculate(ActivityLevel.ExtraActive, FitnessGoal.LoseWeight, 30, options).Goal);
    }
}
