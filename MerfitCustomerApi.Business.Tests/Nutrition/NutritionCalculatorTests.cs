using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Nutrition;

/// <summary>
/// Kisisel hedef hesaplamasi: Mifflin-St Jeor + aktivite + hedef duzeltmesi, makrolarin enerjiyle
/// tutarliligi ve eksik/gecersiz verinin acikca raporlanmasi.
/// </summary>
public class NutritionCalculatorTests
{
    private static readonly NutritionCalculator Calculator = new(Options.Create(new NutritionCalculationOptions()));

    // Guclenme, yag orani bilinmedikce kalori fazlasi almaz: bakim kalorisi referansi olarak kullanilir.
    private static UserProfile Profile(
        FitnessGoal? goal = FitnessGoal.GetStronger,
        decimal? weight = 80m,
        decimal? height = 180m,
        int? age = 30,
        Gender? gender = Gender.Male,
        ActivityLevel? activity = ActivityLevel.ModeratelyActive,
        int? trainingDays = null,
        decimal? targetWeight = null) => new()
    {
        UserId = 1,
        Goal = goal,
        WeightKg = weight,
        HeightCm = height,
        DateOfBirth = age.HasValue ? DateTime.UtcNow.Date.AddYears(-age.Value).AddDays(-1) : null,
        Gender = gender,
        ActivityLevel = activity,
        TrainingDaysPerWeek = trainingDays,
        TargetWeightKg = targetWeight,
    };

    private static NutritionCalculationResult Targets(UserProfile profile)
    {
        var evaluation = Calculator.Evaluate(profile);
        Assert.True(evaluation.IsComplete, string.Join(" ", evaluation.Issues));
        return evaluation.Result!;
    }

    [Fact]
    public void MifflinStJeor_WithActivityMultiplier_MatchesReferenceValue()
    {
        // Erkek, 80 kg, 180 cm, 30 yas: 10*80 + 6.25*180 - 5*30 + 5 = 1780; x1.55 = 2759.
        var result = Targets(Profile());

        Assert.Equal(1780m, result.Bmr);
        Assert.Equal(2759m, result.DailyCalories);
        Assert.Equal(0m, result.GoalCalorieAdjustment);
    }

    [Fact]
    public void Goals_ProduceDirectionallyCorrect_AndConservativeTargets()
    {
        var maintain = Targets(Profile(FitnessGoal.GetStronger));
        var lose = Targets(Profile(FitnessGoal.LoseWeight));
        var muscle = Targets(Profile(FitnessGoal.BuildMuscle));

        Assert.True(lose.DailyCalories < maintain.DailyCalories);
        Assert.True(muscle.DailyCalories > maintain.DailyCalories);

        // Acik en fazla 500 kcal, fazla en fazla 300 kcal.
        Assert.Equal(-500m, lose.GoalCalorieAdjustment);
        Assert.InRange(muscle.GoalCalorieAdjustment, 1m, 300m);

        // Kilo verirken protein, korumadan daha yuksek (kas kaybini sinirlamak icin).
        Assert.True(lose.ProteinTarget > maintain.ProteinTarget);
        Assert.Equal(Math.Round(2.0m * 80m), lose.ProteinTarget);
    }

    [Fact]
    public void Macros_AreEnergyConsistentWithCalories()
    {
        foreach (var goal in Enum.GetValues<FitnessGoal>())
        {
            var result = Targets(Profile(goal));
            var macroEnergy = (result.ProteinTarget * 4) + (result.CarbsTarget * 4) + (result.FatTarget * 9);

            // Yalnizca gram yuvarlamasi kadar fark olabilir (en fazla 0.5*4 + 0.5*4 + 0.5*9).
            Assert.InRange(Math.Abs(macroEnergy - result.DailyCalories), 0m, 8.5m);
            Assert.True(result.ProteinTarget > 0 && result.CarbsTarget > 0 && result.FatTarget > 0);
        }
    }

    [Fact]
    public void BodyMeasurements_AndActivity_ChangeEnergy()
    {
        var baseline = Targets(Profile());

        Assert.True(Targets(Profile(weight: 100m)).DailyCalories > baseline.DailyCalories);
        Assert.True(Targets(Profile(height: 165m)).DailyCalories < baseline.DailyCalories);
        Assert.True(Targets(Profile(age: 60)).DailyCalories < baseline.DailyCalories);
        Assert.True(Targets(Profile(gender: Gender.Female)).DailyCalories < baseline.DailyCalories);
        Assert.True(Targets(Profile(activity: ActivityLevel.Sedentary)).DailyCalories < baseline.DailyCalories);
        Assert.True(Targets(Profile(activity: ActivityLevel.VeryActive)).DailyCalories > baseline.DailyCalories);
    }

    [Fact]
    public void TrainingFrequency_RaisesUnderstatedActivity_AndSubstitutesMissingActivity()
    {
        var sedentary = Targets(Profile(activity: ActivityLevel.Sedentary));
        var sedentaryButTrains5Days = Targets(Profile(activity: ActivityLevel.Sedentary, trainingDays: 5));
        var onlyTrainingDays = Targets(Profile(activity: null, trainingDays: 5));

        Assert.Equal(1.2m, sedentary.ActivityMultiplier);
        Assert.Equal(1.55m, sedentaryButTrains5Days.ActivityMultiplier);
        Assert.Equal(1.55m, onlyTrainingDays.ActivityMultiplier);
    }

    [Fact]
    public void TargetWeight_ThatContradictsGoal_DisablesAdjustment_WithNote()
    {
        var result = Targets(Profile(FitnessGoal.LoseWeight, weight: 70m, targetWeight: 75m));

        Assert.Equal(0m, result.GoalCalorieAdjustment);
        Assert.Single(result.Notes);
    }

    [Fact]
    public void ProteinForHighBmi_UsesReferenceWeight_NotTotalWeight()
    {
        // 170 cm, 140 kg (VKI ~48): protein 27 VKI'deki kiloya gore (~78 kg) hesaplanir.
        var result = Targets(Profile(FitnessGoal.LoseWeight, weight: 140m, height: 170m));

        Assert.Equal(Math.Round(2.0m * 27m * 1.7m * 1.7m), result.ProteinTarget);
    }

    [Fact]
    public void CaloriesNeverDropBelowSafetyFloor()
    {
        var result = Targets(Profile(FitnessGoal.LoseWeight, weight: 45m, height: 150m, age: 70, gender: Gender.Female, activity: ActivityLevel.Sedentary));

        Assert.True(result.DailyCalories >= 1200m);
        Assert.True(result.DailyCalories >= result.Bmr);
    }

    [Fact]
    public void Evaluate_ListsEveryMissingField_InsteadOfUsingDefaults()
    {
        var evaluation = Calculator.Evaluate(Profile(goal: null, weight: null, height: null, age: null, activity: null));

        Assert.False(evaluation.IsComplete);
        Assert.Null(evaluation.Result);
        Assert.Contains(evaluation.Issues, i => i.Contains("Kilo"));
        Assert.Contains(evaluation.Issues, i => i.Contains("Boy"));
        Assert.Contains(evaluation.Issues, i => i.Contains("Dogum tarihi"));
        Assert.Contains(evaluation.Issues, i => i.Contains("hedef"));
        Assert.Contains(evaluation.Issues, i => i.Contains("Aktivite"));
    }

    [Theory]
    [InlineData(10, 180, 30)]   // kilo cok dusuk
    [InlineData(500, 180, 30)]  // kilo cok yuksek
    [InlineData(80, 60, 30)]    // boy cok kisa
    [InlineData(80, 180, 12)]   // yas desteklenmiyor
    public void Evaluate_RejectsImplausibleValues(int weight, int height, int age)
    {
        var evaluation = Calculator.Evaluate(Profile(weight: weight, height: height, age: age));

        Assert.False(evaluation.IsComplete);
        Assert.Single(evaluation.Issues);
    }

    [Fact]
    public void Calculate_IsLenient_ButEqualsEvaluate_WhenInputsAreComplete()
    {
        var complete = Profile(FitnessGoal.BuildMuscle, trainingDays: 4);
        var strict = Targets(complete);
        var lenient = Calculator.Calculate(complete);

        Assert.Equal(strict.DailyCalories, lenient.DailyCalories);
        Assert.Equal(strict.ProteinTarget, lenient.ProteinTarget);
        Assert.Equal(strict.CarbsTarget, lenient.CarbsTarget);
        Assert.Equal(strict.FatTarget, lenient.FatTarget);

        var incomplete = Calculator.Calculate(Profile(goal: null, weight: null, height: null, age: null, activity: null));
        Assert.True(incomplete.DailyCalories >= 1200m);
    }
}
