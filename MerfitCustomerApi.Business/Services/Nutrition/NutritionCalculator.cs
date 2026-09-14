using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// INutritionCalculator'in varsayilan implementasyonu. Mifflin-St Jeor BMR formulu, aktivite
/// carpani ve fitness hedefine gore kalori duzeltmesi kullanarak deterministik bir beslenme
/// hedefi hesaplar. Yapay zeka, makine ogrenmesi veya harici API kullanmaz; tum katsayilar
/// NutritionCalculationOptions uzerinden konfigure edilebilir.
/// </summary>
public class NutritionCalculator : INutritionCalculator
{
    private readonly NutritionCalculationOptions _options;

    public NutritionCalculator(IOptions<NutritionCalculationOptions> options)
    {
        _options = options.Value;
    }

    public NutritionCalculationResult Calculate(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var weightKg = profile.WeightKg ?? _options.DefaultWeightKg;
        var heightCm = profile.HeightCm ?? _options.DefaultHeightCm;
        var age = ResolveAge(profile.DateOfBirth);

        var bmr = CalculateBmr(profile.Gender, weightKg, heightCm, age);
        var activityMultiplier = ResolveActivityMultiplier(profile.ActivityLevel);
        var tdee = bmr * activityMultiplier;

        var goalAdjustment = ResolveGoalAdjustment(profile.Goal);
        var dailyCalories = Math.Max(_options.MinimumDailyCalories, Math.Round(tdee + goalAdjustment, 0));

        var proteinTarget = Math.Round((dailyCalories * _options.ProteinCaloriePercentage) / _options.ProteinCaloriesPerGram, 0);
        var carbsTarget = Math.Round((dailyCalories * _options.CarbsCaloriePercentage) / _options.CarbsCaloriesPerGram, 0);
        var fatTarget = Math.Round((dailyCalories * _options.FatCaloriePercentage) / _options.FatCaloriesPerGram, 0);

        var waterTargetMl = Math.Round(weightKg * _options.WaterMlPerKg, 0);

        return new NutritionCalculationResult
        {
            DailyCalories = dailyCalories,
            ProteinTarget = proteinTarget,
            CarbsTarget = carbsTarget,
            FatTarget = fatTarget,
            WaterTargetMl = waterTargetMl,
        };
    }

    private int ResolveAge(DateTime? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
        {
            return _options.DefaultAge;
        }

        var age = DateTime.UtcNow.Year - dateOfBirth.Value.Year;
        if (DateTime.UtcNow.Date < dateOfBirth.Value.Date.AddYears(age))
        {
            age--;
        }

        return Math.Max(_options.MinimumAge, age);
    }

    private decimal CalculateBmr(Gender? gender, decimal weightKg, decimal heightCm, int age)
    {
        var baseValue = (_options.BmrWeightCoefficient * weightKg)
            + (_options.BmrHeightCoefficient * heightCm)
            - (_options.BmrAgeCoefficient * age);

        var genderConstant = gender switch
        {
            Gender.Male => _options.BmrMaleConstant,
            Gender.Female => _options.BmrFemaleConstant,
            _ => _options.BmrOtherGenderConstant,
        };

        return baseValue + genderConstant;
    }

    private decimal ResolveActivityMultiplier(ActivityLevel? activityLevel)
    {
        if (activityLevel.HasValue && _options.ActivityMultipliers.TryGetValue(activityLevel.Value, out var multiplier))
        {
            return multiplier;
        }

        return _options.DefaultActivityMultiplier;
    }

    private decimal ResolveGoalAdjustment(FitnessGoal? goal)
    {
        if (goal.HasValue && _options.GoalCalorieAdjustments.TryGetValue(goal.Value, out var adjustment))
        {
            return adjustment;
        }

        return 0m;
    }
}
