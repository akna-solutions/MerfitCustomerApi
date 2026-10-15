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
        foreach (var goal in FitnessGoals.Primary)
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
            Hypertrophy = 80, Strength = 25, Cardio = -5,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BuildMuscle") && f.Contains("Cardio") && f.Contains("negatif"));
    }

    [Fact]
    public void Validate_Fails_WhenGoalWeightsDoNotSumToHundred()
    {
        var options = new WorkoutPlanGenerationOptions();
        options.AttributeWeightsByGoal[FitnessGoal.LoseWeight].Cardio = 35;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("LoseWeight") && f.Contains("105"));
    }

    [Fact]
    public void Validate_Fails_WhenAPrimaryGoalIsMissing()
    {
        var options = new WorkoutPlanGenerationOptions();
        options.AttributeWeightsByGoal.Remove(FitnessGoal.GetStronger);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("GetStronger"));
    }

    [Fact]
    public void Validate_IgnoresLegacyGoals_WhichAreNormalizedBeforeScoring()
    {
        var options = new WorkoutPlanGenerationOptions();
        Assert.False(options.AttributeWeightsByGoal.ContainsKey(FitnessGoal.ImproveEndurance));

        // Eski bir appsettings'ten kalan hatali eski anahtar uygulamayi durdurmaz.
        options.AttributeWeightsByGoal[FitnessGoal.MaintainWeight] = new GoalAttributeWeights { Hypertrophy = 10 };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_Fails_WhenCardioSessionsOutOfRange()
    {
        var options = new WorkoutPlanGenerationOptions();
        options.CardioSessionsPerWeekByGoal[FitnessGoal.LoseWeight] = 8;

        Assert.True(_validator.Validate(null, options).Failed);
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
        Assert.Equal((65, 25, 10), (buildMuscle.Hypertrophy, buildMuscle.Strength, buildMuscle.Cardio));
        var getStronger = options.AttributeWeightsByGoal[FitnessGoal.GetStronger];
        Assert.Equal((25, 65, 10), (getStronger.Hypertrophy, getStronger.Strength, getStronger.Cardio));
        var loseWeight = options.AttributeWeightsByGoal[FitnessGoal.LoseWeight];
        Assert.Equal((45, 25, 30), (loseWeight.Hypertrophy, loseWeight.Strength, loseWeight.Cardio));
        Assert.Equal(3, options.CardioSessionsPerWeekByGoal[FitnessGoal.LoseWeight]);
    }
}
