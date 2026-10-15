using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Personalization;

/// <summary>
/// FitScore = (Hipertrofi*W_h + Kuvvet*W_s + Kardiyo*W_c) / 100 formulunu, ikincil ozelliklerin
/// sayilmadigini ve eksik ana puanin antrenmani yukseltmedigini dogrular.
/// </summary>
public class WorkoutGoalFitCalculatorTests
{
    private static readonly WorkoutPlanGenerationOptions DefaultOptions = new();

    private static GoalAttributeWeights WeightsFor(FitnessGoal goal) => DefaultOptions.AttributeWeightsByGoal[goal];

    private static Dictionary<WorkoutAttributeType, int> AllScores(int value) =>
        Enum.GetValues<WorkoutAttributeType>().ToDictionary(t => t, _ => value);

    [Fact]
    public void Calculate_UsesOnlyPrimaryAttributes()
    {
        var scores = new Dictionary<WorkoutAttributeType, int>
        {
            [WorkoutAttributeType.Hypertrophy] = 90,
            [WorkoutAttributeType.Strength] = 70,
            [WorkoutAttributeType.Cardio] = 20,
            [WorkoutAttributeType.Endurance] = 100,
            [WorkoutAttributeType.Mobility] = 100,
            [WorkoutAttributeType.CoreStability] = 100,
        };

        var result = WorkoutGoalFitCalculator.Calculate(scores, WeightsFor(FitnessGoal.BuildMuscle));

        // 90*65 + 70*25 + 20*10 = 7800 -> /100 = 78; ikincil ozellikler katilmaz.
        Assert.Equal(78m, result);
    }

    [Theory]
    [InlineData(FitnessGoal.BuildMuscle, 65, 25, 10)]
    [InlineData(FitnessGoal.GetStronger, 25, 65, 10)]
    [InlineData(FitnessGoal.LoseWeight, 45, 25, 30)]
    public void DefaultWeights_MatchTheAgreedTable(FitnessGoal goal, int hypertrophy, int strength, int cardio)
    {
        var weights = WeightsFor(goal);
        Assert.Equal((hypertrophy, strength, cardio), (weights.Hypertrophy, weights.Strength, weights.Cardio));
        Assert.Equal(100, weights.Total);
    }

    [Theory]
    [InlineData(FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.GetStronger)]
    [InlineData(FitnessGoal.LoseWeight)]
    public void Calculate_ReturnsZeroAndHundred_AtTheExtremes(FitnessGoal goal)
    {
        Assert.Equal(0m, WorkoutGoalFitCalculator.Calculate(AllScores(0), WeightsFor(goal)));
        Assert.Equal(100m, WorkoutGoalFitCalculator.Calculate(AllScores(100), WeightsFor(goal)));
    }

    [Fact]
    public void Calculate_CountsMissingPrimaryScoresAsZero_SoPartialScoresCannotOverboost()
    {
        var onlyHypertrophy = new Dictionary<WorkoutAttributeType, int> { [WorkoutAttributeType.Hypertrophy] = 95 };
        var fullyScored = new Dictionary<WorkoutAttributeType, int>
        {
            [WorkoutAttributeType.Hypertrophy] = 85,
            [WorkoutAttributeType.Strength] = 70,
            [WorkoutAttributeType.Cardio] = 30,
        };

        var partial = WorkoutGoalFitCalculator.Calculate(onlyHypertrophy, WeightsFor(FitnessGoal.BuildMuscle));
        var full = WorkoutGoalFitCalculator.Calculate(fullyScored, WeightsFor(FitnessGoal.BuildMuscle));

        Assert.Equal(61.75m, partial); // 95*65/100, normalize edilmez
        Assert.Equal(75.75m, full);    // 85*65 + 70*25 + 30*10 = 7575
        Assert.True(full > partial);
        Assert.False(WorkoutGoalFitCalculator.HasAllPrimaryScores(onlyHypertrophy));
        Assert.True(WorkoutGoalFitCalculator.HasAllPrimaryScores(fullyScored));
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenWorkoutHasNoScores()
    {
        Assert.Null(WorkoutGoalFitCalculator.Calculate(new Dictionary<WorkoutAttributeType, int>(), WeightsFor(FitnessGoal.BuildMuscle)));
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenOnlySecondaryAttributesAreScored()
    {
        var scores = new Dictionary<WorkoutAttributeType, int>
        {
            [WorkoutAttributeType.Endurance] = 90,
            [WorkoutAttributeType.Mobility] = 90,
        };

        Assert.Null(WorkoutGoalFitCalculator.Calculate(scores, WeightsFor(FitnessGoal.LoseWeight)));
    }
}
