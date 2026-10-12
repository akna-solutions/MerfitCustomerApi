using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Progress;

/// <summary>
/// Kullanicinin hedefine gore otomatik hedef kilo hesaplar. Kural tabanlidir, AI kullanmaz:
/// <list type="bullet">
/// <item>Kilo verme: ideal kilo = BMI 22 (saglikli araligin ortasi) x boy². Zaten ideal veya alti ise
/// saglikli alt sinirin (BMI 18.5) altina inmeden 2 kg.</item>
/// <item>Kas kazanma: deneyime gore ~6 aylik gercekci yagsiz kutle artisi (baslangic %6, orta %3,
/// ileri %1.5); en az saglikli alt sinir (BMI 18.5).</item>
/// <item>Guclenme: kas kazanmanin yarisi kadar artis.</item>
/// <item>Kilo koruma, kondisyon, dayaniklilik: mevcut kilo.</item>
/// </list>
/// Sonuc 0.5 kg'a yuvarlanir. Boy veya kilo bilinmiyorsa null doner (tahmin yapilmaz).
/// </summary>
public static class TargetWeightCalculator
{
    public const decimal IdealBmi = 22m;
    public const decimal HealthyBmiMin = 18.5m;
    public const decimal MinimumLossKg = 2m;

    /// <summary>Yaklasik 6 aylik ufukta vucut agirligina oranla gercekci kas artisi.</summary>
    private static decimal MuscleGainRatio(ExperienceLevel? level) => level switch
    {
        ExperienceLevel.Advanced => 0.015m,
        ExperienceLevel.Intermediate => 0.03m,
        _ => 0.06m,
    };

    public static decimal? Calculate(FitnessGoal goal, decimal? weightKg, decimal? heightCm, ExperienceLevel? experience)
    {
        if (weightKg is not (> 20 and < 400) || heightCm is not (> 80 and < 260))
        {
            return null;
        }

        var weight = weightKg.Value;
        var heightM = heightCm.Value / 100m;
        var heightSquared = heightM * heightM;
        var healthyMin = HealthyBmiMin * heightSquared;

        var target = goal switch
        {
            FitnessGoal.LoseWeight => weight > IdealBmi * heightSquared
                ? IdealBmi * heightSquared
                : Math.Max(healthyMin, weight - MinimumLossKg),
            FitnessGoal.BuildMuscle => Math.Max(healthyMin, weight * (1 + MuscleGainRatio(experience))),
            FitnessGoal.GetStronger => Math.Max(healthyMin, weight * (1 + MuscleGainRatio(experience) / 2)),
            _ => weight,
        };

        // Kilo vermede hedef mevcut kilonun ustune cikamaz (zaten saglikli alt sinirdaysa korur).
        if (goal == FitnessGoal.LoseWeight)
        {
            target = Math.Min(target, weight);
        }

        return RoundToHalf(target);
    }

    public static decimal RoundToHalf(decimal value) => Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
}
