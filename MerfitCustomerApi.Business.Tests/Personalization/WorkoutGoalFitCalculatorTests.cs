using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Personalization;

/// <summary>
/// FitScore = SUM(AttributeScore * GoalWeight) / SUM(GoalWeight) formulunu ve eksik puan stratejisini dogrular.
/// </summary>
public class WorkoutGoalFitCalculatorTests
{
    private static readonly WorkoutPlanGenerationOptions DefaultOptions = new();

    private static GoalAttributeWeights WeightsFor(FitnessGoal goal) => DefaultOptions.AttributeWeightsByGoal[goal];

    private static Dictionary<WorkoutAttributeType, int> AllScores(int value) =>
        Enum.GetValues<WorkoutAttributeType>().ToDictionary(t => t, _ => value);

    [Fact]
    public void Calculate_ReturnsWeightedSum_WhenAllSixAttributesAreScored()
    {
        var scores = new Dictionary<WorkoutAttributeType, int>
        {
            [WorkoutAttributeType.Hypertrophy] = 90,
            [WorkoutAttributeType.Strength] = 75,
            [WorkoutAttributeType.Cardio] = 20,
            [WorkoutAttributeType.Endurance] = 40,
            [WorkoutAttributeType.Mobility] = 30,
            [WorkoutAttributeType.CoreStability] = 60,
        };

        var result = WorkoutGoalFitCalculator.Calculate(scores, WeightsFor(FitnessGoal.BuildMuscle));

        // 90*50 + 75*20 + 20*5 + 40*5 + 30*10 + 60*10 = 7200 -> /100 = 72
        Assert.Equal(72m, result);
    }

    [Fact]
    public void Calculate_HypertrophyContribution_MatchesSpecExample()
    {
        var scores = new Dictionary<WorkoutAttributeType, int>(AllScores(0))
        {
            [WorkoutAttributeType.Hypertrophy] = 90,
        };

        var result = WorkoutGoalFitCalculator.Calculate(scores, WeightsFor(FitnessGoal.BuildMuscle));

        // Yalnizca Hypertrophy katkisi: 90 x 50 / 100 = 45
        Assert.Equal(45m, result);
    }

    [Theory]
    [InlineData(FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.GetStronger)]
    [InlineData(FitnessGoal.LoseWeight)]
    [InlineData(FitnessGoal.ImproveFitness)]
    [InlineData(FitnessGoal.MaintainWeight)]
    [InlineData(FitnessGoal.ImproveEndurance)]
    public void Calculate_ReturnsZero_WhenAllScoresAreZero(FitnessGoal goal)
    {
        Assert.Equal(0m, WorkoutGoalFitCalculator.Calculate(AllScores(0), WeightsFor(goal)));
    }

    [Theory]
    [InlineData(FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.GetStronger)]
    [InlineData(FitnessGoal.LoseWeight)]
    [InlineData(FitnessGoal.ImproveFitness)]
    [InlineData(FitnessGoal.MaintainWeight)]
    [InlineData(FitnessGoal.ImproveEndurance)]
    public void Calculate_ReturnsHundred_WhenAllScoresAreHundred(FitnessGoal goal)
    {
        Assert.Equal(100m, WorkoutGoalFitCalculator.Calculate(AllScores(100), WeightsFor(goal)));
    }

    [Fact]
    public void Calculate_NormalizesWeightsOverScoredAttributes_WhenSomeAttributesAreMissing()
    {
        // BuildMuscle: Hypertrophy 50, Strength 20 -> (80*50 + 50*20) / 70
        var scores = new Dictionary<WorkoutAttributeType, int>
        {
            [WorkoutAttributeType.Hypertrophy] = 80,
            [WorkoutAttributeType.Strength] = 50,
        };

        var result = WorkoutGoalFitCalculator.Calculate(scores, WeightsFor(FitnessGoal.BuildMuscle));

        Assert.Equal(5000m / 70m, result);
        Assert.InRange(result!.Value, 0m, 100m);
    }

    [Fact]
    public void Calculate_DoesNotTreatMissingAttributesAsZero()
    {
        var partial = new Dictionary<WorkoutAttributeType, int> { [WorkoutAttributeType.Hypertrophy] = 80 };
        var withExplicitZeros = new Dictionary<WorkoutAttributeType, int>(AllScores(0)) { [WorkoutAttributeType.Hypertrophy] = 80 };

        var partialResult = WorkoutGoalFitCalculator.Calculate(partial, WeightsFor(FitnessGoal.BuildMuscle));
        var zeroFilledResult = WorkoutGoalFitCalculator.Calculate(withExplicitZeros, WeightsFor(FitnessGoal.BuildMuscle));

        Assert.Equal(80m, partialResult);
        Assert.Equal(40m, zeroFilledResult);
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenWorkoutHasNoScores()
    {
        Assert.Null(WorkoutGoalFitCalculator.Calculate(new Dictionary<WorkoutAttributeType, int>(), WeightsFor(FitnessGoal.BuildMuscle)));
    }

    [Fact]
    public void Calculate_ReturnsNull_WhenScoredAttributesHaveZeroWeightForGoal()
    {
        var weights = new GoalAttributeWeights { Hypertrophy = 100 };
        var scores = new Dictionary<WorkoutAttributeType, int> { [WorkoutAttributeType.Cardio] = 90 };

        Assert.Null(WorkoutGoalFitCalculator.Calculate(scores, weights));
    }
}
