using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Nutrition;

/// <summary>
/// Hedef ve yag orani uyumu: Kas Gelistirme'de kontrollu fazla (yag orani yuksekse bakim), Guclenme'de
/// yag orani bilinmedikce bakim, Yag Kaybi'nda orta acik ve yeterli protein; dusuk yag oraninda acik
/// artirilmaz. BMR alt siniri korunur.
/// </summary>
public class NutritionBodyCompositionRulesTests
{
    private static readonly NutritionCalculator Calculator = new(Options.Create(new NutritionCalculationOptions()));
    private static readonly DateTime Now = DateTime.UtcNow;

    private static UserProfile Profile(FitnessGoal goal) => new()
    {
        UserId = 1,
        Goal = goal,
        WeightKg = 80,
        HeightCm = 180,
        DateOfBirth = Now.Date.AddYears(-30).AddDays(-1),
        Gender = Gender.Male,
        ActivityLevel = ActivityLevel.ModeratelyActive,
    };

    private static BodyFatAssessment BodyFat(decimal percentage) =>
        BodyFatAssessor.Assess(Gender.Male, Now.Date.AddYears(-30).AddDays(-1), 180,
            new[] { new BodyMeasurement { Id = 1, UserId = 1, RecordedAt = Now.AddDays(-1), BodyFatPercentage = percentage } },
            Now, new BodyCompositionOptions());

    private static NutritionCalculationResult Calc(FitnessGoal goal, BodyFatAssessment? bodyFat = null) =>
        Calculator.Calculate(Profile(goal), bodyFat)!;

    [Fact]
    public void BuildMuscle_GetsControlledSurplus_WhenBodyFatIsNotHigh()
    {
        Assert.InRange(Calc(FitnessGoal.BuildMuscle).GoalCalorieAdjustment, 1m, 300m);
        Assert.InRange(Calc(FitnessGoal.BuildMuscle, BodyFat(15)).GoalCalorieAdjustment, 1m, 300m);
    }

    [Fact]
    public void BuildMuscle_WithHighBodyFat_GetsMaintenance_NotSurplus()
    {
        var result = Calc(FitnessGoal.BuildMuscle, BodyFat(28));

        Assert.Equal(0m, result.GoalCalorieAdjustment);
        Assert.Contains(result.Notes, n => n.Contains("yag oranin yuksek"));
    }

    [Fact]
    public void GetStronger_DoesNotGetAutomaticSurplus_WithoutKnownBodyFat()
    {
        Assert.Equal(0m, Calc(FitnessGoal.GetStronger).GoalCalorieAdjustment);
        Assert.True(Calc(FitnessGoal.GetStronger, BodyFat(14)).GoalCalorieAdjustment > 0m);
        Assert.Equal(0m, Calc(FitnessGoal.GetStronger, BodyFat(26)).GoalCalorieAdjustment);
    }

    [Fact]
    public void LoseWeight_GetsModerateDeficit_AndAdequateProtein()
    {
        var result = Calc(FitnessGoal.LoseWeight, BodyFat(22));

        Assert.InRange(result.GoalCalorieAdjustment, -500m, -1m);
        Assert.Equal(160m, result.ProteinTarget); // 2.0 g/kg
        Assert.True(result.DailyCalories >= result.Bmr);
    }

    [Fact]
    public void LoseWeight_WithLowBodyFat_DoesNotPushFurtherDeficit_AndSuggestsReevaluation()
    {
        var result = Calc(FitnessGoal.LoseWeight, BodyFat(6));

        Assert.Equal(0m, result.GoalCalorieAdjustment);
        Assert.NotEmpty(result.Notes);
    }

    [Theory]
    [InlineData(FitnessGoal.MaintainWeight)]
    [InlineData(FitnessGoal.ImproveFitness)]
    [InlineData(FitnessGoal.ImproveEndurance)]
    public void LegacyGoals_AreCalculatedAsBuildMuscle(FitnessGoal legacy)
    {
        var legacyResult = Calc(legacy, BodyFat(15));
        var buildMuscle = Calc(FitnessGoal.BuildMuscle, BodyFat(15));

        Assert.Equal(buildMuscle.DailyCalories, legacyResult.DailyCalories);
        Assert.Equal(buildMuscle.ProteinTarget, legacyResult.ProteinTarget);
    }
}
