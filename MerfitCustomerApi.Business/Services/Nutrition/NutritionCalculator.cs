using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// INutritionCalculator'in varsayilan implementasyonu. Yapay zeka, makine ogrenmesi veya harici API
/// kullanmaz; tum katsayilar NutritionCalculationOptions uzerinden konfigure edilebilir.
///
/// Algoritma:
/// 1. BMR = Mifflin-St Jeor (kilo, boy, yas, cinsiyet; cinsiyet belirtilmemisse erkek/kadin sabitlerinin ortalamasi).
/// 2. Aktivite carpani = aktivite seviyesinin carpani ile haftalik antrenman gununun isaret ettigi carpanin buyugu.
/// 3. TDEE = BMR x aktivite carpani.
/// 4. Hedef duzeltmesi = TDEE x hedef yuzdesi, mutlak degeri hedefe ozel ust sinirla kirpilir. Hedef kilo
///    mevcut kiloyla celisiyorsa (kilo vermek isteyip hedef kilo daha yuksekse vb.) duzeltme uygulanmaz.
///    Hedef once uc ana hedeften birine eslenir (FitnessGoals.Normalize). Vucut kompozisyonu kurallari:
///    - Yag orani yas/cinsiyete gore yuksek araliktaysa kalori fazlasi uygulanmaz (bakim kalorisi).
///    - SurplusRequiresKnownBodyFat'taki hedeflerde (varsayilan Guclenme) kalori fazlasi yalnizca yag orani
///      biliniyor ve yuksek degilse uygulanir; her kullaniciya otomatik fazla verilmez.
///    - Yag Kaybi'nda yag orani dusuk araliktaysa kalori acigi uygulanmaz ve hedefi yeniden degerlendirme
///      notu eklenir; daha agresif acik onerilmez.
/// 5. Kalori = max(TDEE + duzeltme, BMR, MinimumDailyCalories).
/// 6. Protein = hedefe gore g/kg x referans kilo (yuksek VKI'de ProteinReferenceMaxBmi'deki kilo), kalorinin
///    en fazla MaxProteinCaloriePercentage'i.
/// 7. Yag = hedefe gore kalori payi; karbonhidrat MinimumCarbsGrams altina duserse yag, en az
///    MinimumFatCaloriePercentage olacak sekilde azaltilir.
/// 8. Karbonhidrat = kalan enerji / 4. Boylece P*4 + K*4 + Y*9 = kalori (yalnizca gram yuvarlamasi kadar fark).
/// </summary>
public class NutritionCalculator : INutritionCalculator
{
    private readonly NutritionCalculationOptions _options;

    public NutritionCalculator(IOptions<NutritionCalculationOptions> options)
    {
        _options = options.Value;
    }

    public NutritionCalculationResult Calculate(UserProfile profile, BodyFatAssessment? bodyFat = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var weightKg = IsPlausibleWeight(profile.WeightKg) ? profile.WeightKg!.Value : _options.DefaultWeightKg;
        var heightCm = IsPlausibleHeight(profile.HeightCm) ? profile.HeightCm!.Value : _options.DefaultHeightCm;
        var age = profile.DateOfBirth.HasValue
            ? Math.Max(_options.MinimumAge, CalculateAge(profile.DateOfBirth.Value))
            : _options.DefaultAge;
        var activityMultiplier = ResolveActivityMultiplier(profile.ActivityLevel, profile.TrainingDaysPerWeek)
            ?? _options.DefaultActivityMultiplier;
        var targetWeightKg = IsPlausibleWeight(profile.TargetWeightKg) ? profile.TargetWeightKg : null;

        return Compute(profile.Gender, weightKg, heightCm, age, activityMultiplier, profile.Goal, targetWeightKg, bodyFat);
    }

    public NutritionTargetEvaluation Evaluate(UserProfile profile, BodyFatAssessment? bodyFat = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var issues = new List<string>();

        if (!profile.WeightKg.HasValue)
        {
            issues.Add("Kilo bilgisi eksik.");
        }
        else if (!IsPlausibleWeight(profile.WeightKg))
        {
            issues.Add($"Kilo {_options.PlausibleMinWeightKg:0}-{_options.PlausibleMaxWeightKg:0} kg araliginda olmali.");
        }

        if (!profile.HeightCm.HasValue)
        {
            issues.Add("Boy bilgisi eksik.");
        }
        else if (!IsPlausibleHeight(profile.HeightCm))
        {
            issues.Add($"Boy {_options.PlausibleMinHeightCm:0}-{_options.PlausibleMaxHeightCm:0} cm araliginda olmali.");
        }

        int? age = null;
        if (!profile.DateOfBirth.HasValue)
        {
            issues.Add("Dogum tarihi (yas) bilgisi eksik.");
        }
        else
        {
            age = CalculateAge(profile.DateOfBirth.Value);
            if (age < _options.PlausibleMinAge || age > _options.PlausibleMaxAge)
            {
                issues.Add($"Kisisel beslenme hedefi {_options.PlausibleMinAge}-{_options.PlausibleMaxAge} yas arasi icin hesaplanabilir.");
            }
        }

        if (!profile.Goal.HasValue)
        {
            issues.Add("Fitness hedefi secilmemis.");
        }

        var activityMultiplier = ResolveActivityMultiplier(profile.ActivityLevel, profile.TrainingDaysPerWeek);
        if (activityMultiplier is null)
        {
            issues.Add("Aktivite seviyesi veya haftalik antrenman gunu bilgisi eksik.");
        }

        if (profile.TargetWeightKg.HasValue && !IsPlausibleWeight(profile.TargetWeightKg))
        {
            issues.Add($"Hedef kilo {_options.PlausibleMinWeightKg:0}-{_options.PlausibleMaxWeightKg:0} kg araliginda olmali.");
        }

        if (issues.Count > 0)
        {
            return new NutritionTargetEvaluation { Issues = issues };
        }

        return new NutritionTargetEvaluation
        {
            Result = Compute(
                profile.Gender,
                profile.WeightKg!.Value,
                profile.HeightCm!.Value,
                age!.Value,
                activityMultiplier!.Value,
                profile.Goal,
                profile.TargetWeightKg,
                bodyFat),
        };
    }

    private NutritionCalculationResult Compute(
        Gender? gender,
        decimal weightKg,
        decimal heightCm,
        int age,
        decimal activityMultiplier,
        FitnessGoal? rawGoal,
        decimal? targetWeightKg,
        BodyFatAssessment? bodyFat)
    {
        var notes = new List<string>();
        FitnessGoal? goal = rawGoal.HasValue ? FitnessGoals.Normalize(rawGoal.Value) : null;

        var bmr = CalculateBmr(gender, weightKg, heightCm, age);
        var tdee = bmr * activityMultiplier;

        var adjustment = ResolveGoalAdjustment(goal, tdee);
        adjustment = ApplyBodyCompositionRules(goal, adjustment, bodyFat, notes);
        if (adjustment < 0 && targetWeightKg.HasValue && targetWeightKg.Value >= weightKg)
        {
            notes.Add("Hedef kilonuz mevcut kilonuzdan dusuk olmadigi icin kalori acigi uygulanmadi.");
            adjustment = 0m;
        }
        else if (adjustment > 0 && targetWeightKg.HasValue && targetWeightKg.Value < weightKg)
        {
            notes.Add("Hedef kilonuz mevcut kilonuzdan dusuk oldugu icin kalori fazlasi uygulanmadi.");
            adjustment = 0m;
        }

        var dailyCalories = Math.Round(tdee + adjustment, 0);
        var floor = Math.Max(_options.MinimumDailyCalories, Math.Round(bmr, 0));
        if (dailyCalories < floor)
        {
            notes.Add("Gunluk kalori guvenli alt sinira yukseltildi.");
            dailyCalories = floor;
        }

        var proteinPerKg = goal.HasValue && _options.ProteinGramsPerKg.TryGetValue(goal.Value, out var configuredProtein)
            ? configuredProtein
            : _options.DefaultProteinGramsPerKg;
        var heightM = heightCm / 100m;
        var referenceWeightKg = Math.Min(weightKg, _options.ProteinReferenceMaxBmi * heightM * heightM);
        var proteinGrams = Math.Min(
            proteinPerKg * referenceWeightKg,
            dailyCalories * _options.MaxProteinCaloriePercentage / _options.ProteinCaloriesPerGram);
        proteinGrams = Math.Round(proteinGrams, 0);

        var fatPercentage = goal.HasValue && _options.FatCaloriePercentages.TryGetValue(goal.Value, out var configuredFat)
            ? configuredFat
            : _options.DefaultFatCaloriePercentage;
        var fatGrams = dailyCalories * fatPercentage / _options.FatCaloriesPerGram;

        var carbsGrams = CarbsFromRemainder(dailyCalories, proteinGrams, fatGrams);
        if (carbsGrams < _options.MinimumCarbsGrams)
        {
            // Karbonhidrat cok dusuk: yagi, en az MinimumFatCaloriePercentage kalacak sekilde azalt.
            var minFatGrams = dailyCalories * _options.MinimumFatCaloriePercentage / _options.FatCaloriesPerGram;
            var missingCarbCalories = (_options.MinimumCarbsGrams - carbsGrams) * _options.CarbsCaloriesPerGram;
            fatGrams = Math.Max(minFatGrams, fatGrams - (missingCarbCalories / _options.FatCaloriesPerGram));
            carbsGrams = CarbsFromRemainder(dailyCalories, proteinGrams, fatGrams);
        }

        fatGrams = Math.Round(fatGrams, 0);
        carbsGrams = Math.Max(0m, Math.Round(CarbsFromRemainder(dailyCalories, proteinGrams, fatGrams), 0));

        return new NutritionCalculationResult
        {
            DailyCalories = dailyCalories,
            ProteinTarget = proteinGrams,
            CarbsTarget = carbsGrams,
            FatTarget = fatGrams,
            WaterTargetMl = Math.Round(weightKg * _options.WaterMlPerKg, 0),
            Bmr = Math.Round(bmr, 0),
            ActivityMultiplier = activityMultiplier,
            Tdee = Math.Round(tdee, 0),
            GoalCalorieAdjustment = Math.Round(adjustment, 0),
            Notes = notes,
        };
    }

    private decimal CarbsFromRemainder(decimal dailyCalories, decimal proteinGrams, decimal fatGrams)
    {
        var remaining = dailyCalories
            - (proteinGrams * _options.ProteinCaloriesPerGram)
            - (fatGrams * _options.FatCaloriesPerGram);
        return remaining / _options.CarbsCaloriesPerGram;
    }

    private bool IsPlausibleWeight(decimal? weightKg) =>
        weightKg.HasValue && weightKg.Value >= _options.PlausibleMinWeightKg && weightKg.Value <= _options.PlausibleMaxWeightKg;

    private bool IsPlausibleHeight(decimal? heightCm) =>
        heightCm.HasValue && heightCm.Value >= _options.PlausibleMinHeightCm && heightCm.Value <= _options.PlausibleMaxHeightCm;

    private static int CalculateAge(DateTime dateOfBirth)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - dateOfBirth.Year;
        if (today < dateOfBirth.Date.AddYears(age))
        {
            age--;
        }

        return age;
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

    /// <summary>
    /// Aktivite seviyesi ve haftalik antrenman gunlerinden carpani cozer; ikisi de varsa buyugunu alir.
    /// Ikisi de yoksa null doner.
    /// </summary>
    private decimal? ResolveActivityMultiplier(ActivityLevel? activityLevel, int? trainingDaysPerWeek)
    {
        decimal? fromLevel = activityLevel.HasValue && _options.ActivityMultipliers.TryGetValue(activityLevel.Value, out var levelMultiplier)
            ? levelMultiplier
            : null;
        decimal? fromTraining = trainingDaysPerWeek.HasValue
            && _options.TrainingDaysActivityMultipliers.TryGetValue(trainingDaysPerWeek.Value, out var trainingMultiplier)
            ? trainingMultiplier
            : null;

        if (fromLevel.HasValue && fromTraining.HasValue)
        {
            return Math.Max(fromLevel.Value, fromTraining.Value);
        }

        return fromLevel ?? fromTraining;
    }

    /// <summary>
    /// Vucut kompozisyonuna gore kalori fazlasi/acigi kararini sinirlar. Yalnizca bilinen (Unknown olmayan)
    /// degerlendirme dusuk/yuksek sonuc verebilir; eksik veriyle kural uydurulmaz. Fazla ya da acik hicbir
    /// zaman buyutulmez, yalnizca kaldirilir.
    /// </summary>
    private decimal ApplyBodyCompositionRules(FitnessGoal? goal, decimal adjustment, BodyFatAssessment? bodyFat, List<string> notes)
    {
        if (adjustment > 0)
        {
            if (bodyFat?.IsHigh == true)
            {
                notes.Add("Vucut yag oranin yuksek aralikta gorundugu icin kalori fazlasi uygulanmadi; bakim kalorisi ve yeterli protein hedeflendi.");
                return 0m;
            }

            var bodyFatKnown = bodyFat is not null && bodyFat.Category != BodyFatCategory.Unknown;
            if (goal.HasValue && _options.SurplusRequiresKnownBodyFat.Contains(goal.Value) && !bodyFatKnown)
            {
                notes.Add("Vucut yag oranin bilinmedigi icin kalori fazlasi uygulanmadi; olcumlerini girersen hedeflerin buna gore guncellenir.");
                return 0m;
            }
        }

        if (adjustment < 0 && goal == FitnessGoal.LoseWeight && bodyFat?.IsLow == true)
        {
            notes.Add("Olcumlerine gore vucut yag oranin dusuk aralikta gorundugu icin kalori acigi uygulanmadi. Hedefini yeniden degerlendirmeni oneririz; olcum tahmini bir deger olabilir.");
            return 0m;
        }

        return adjustment;
    }

    private decimal ResolveGoalAdjustment(FitnessGoal? goal, decimal tdee)
    {
        if (!goal.HasValue || !_options.GoalCalorieAdjustmentPercentages.TryGetValue(goal.Value, out var percentage))
        {
            return 0m;
        }

        var adjustment = tdee * percentage;
        if (_options.GoalCalorieAdjustmentCaps.TryGetValue(goal.Value, out var cap))
        {
            adjustment = Math.Clamp(adjustment, -cap, cap);
        }

        return adjustment;
    }
}
