using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Personalization;

/// <summary>
/// Hedef agirlik profillerinin (AttributeWeightsByGoal) dogrulamasini ve appsettings.json'daki
/// gercek degerlerin gecerli oldugunu dogrular.
/// </summary>
public class WorkoutPlanGenerationOptionsValidatorTests
{
    private readonly WorkoutPlanGenerationOptionsValidator _validator = new();

    [Fact]
    public void DefaultOptions_AreValid_AndEveryGoalSumsToHundred()
    {
        var options = new WorkoutPlanGenerationOptions();

        Assert.True(_validator.Validate(null, options).Succeeded);
        foreach (var goal in Enum.GetValues<FitnessGoal>())
        {
            Assert.Equal(100, options.AttributeWeightsByGoal[goal].Total);
        }
    }

    [Fact]
    public void Validate_Fails_WhenWeightIsNegative()
    {
        var options = new WorkoutPlanGenerationOptions();
        // Toplam yine 100 kalir; yalnizca negatif deger hatasi beklenir.
        options.AttributeWeightsByGoal[FitnessGoal.BuildMuscle] = new GoalAttributeWeights
        {
            Hypertrophy = 60, Strength = 20, Cardio = -5, Endurance = 5, Mobility = 10, CoreStability = 10,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BuildMuscle") && f.Contains("Cardio") && f.Contains("negatif"));
    }

    [Fact]
    public void Validate_Fails_WhenGoalWeightsDoNotSumToHundred()
    {
        var options = new WorkoutPlanGenerationOptions();
        options.AttributeWeightsByGoal[FitnessGoal.LoseWeight].Cardio = 40;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("LoseWeight") && f.Contains("105"));
    }

    [Fact]
    public void Validate_Fails_WhenAGoalIsMissing()
    {
        var options = new WorkoutPlanGenerationOptions();
        options.AttributeWeightsByGoal.Remove(FitnessGoal.ImproveEndurance);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ImproveEndurance"));
    }

    [Fact]
    public void Validate_Fails_WhenGoalFitMaxScoreIsNegative()
    {
        var options = new WorkoutPlanGenerationOptions { GoalFitMaxScore = -1 };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validate_Fails_WhenInvalidValueComesFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkoutPlanGeneration:AttributeWeightsByGoal:GetStronger:Strength"] = "70",
            })
            .Build();
        var options = new WorkoutPlanGenerationOptions();
        configuration.GetSection(WorkoutPlanGenerationOptions.SectionName).Bind(options);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("GetStronger"));
    }

    [Fact]
    public void AppSettingsJson_BindsToValidWeights_MatchingTheAgreedTable()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var options = new WorkoutPlanGenerationOptions();
        configuration.GetSection(WorkoutPlanGenerationOptions.SectionName).Bind(options);

        Assert.True(_validator.Validate(null, options).Succeeded);

        var buildMuscle = options.AttributeWeightsByGoal[FitnessGoal.BuildMuscle];
        Assert.Equal((50, 20, 5, 5, 10, 10), (buildMuscle.Hypertrophy, buildMuscle.Strength, buildMuscle.Cardio, buildMuscle.Endurance, buildMuscle.Mobility, buildMuscle.CoreStability));
        var improveEndurance = options.AttributeWeightsByGoal[FitnessGoal.ImproveEndurance];
        Assert.Equal((10, 10, 25, 35, 10, 10), (improveEndurance.Hypertrophy, improveEndurance.Strength, improveEndurance.Cardio, improveEndurance.Endurance, improveEndurance.Mobility, improveEndurance.CoreStability));
    }
}
