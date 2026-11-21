using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Activity;

/// <summary>
/// Gunluk adim hedefini profilden belirler; kullanici hedef secmez. Saf ve deterministiktir.
///
/// Kural (500'e yuvarlanir, AutoGoalMin-AutoGoalMax arasina kirpilir):
/// 1. Aktivite seviyesine gore baz: hareketsiz 6.000, hafif 7.500, orta 9.000, yuksek 10.000, cok yuksek 11.000.
///    Aktivite seviyesi yoksa DefaultDailyGoal.
/// 2. Kilo verme (Yag Kaybi) hedefinde +1.500 adim (enerji harcamasina gunluk hareketle destek).
/// 3. 60 yas ve uzeri -1.000; 65 yas ve uzeri en fazla 8.000 (yasli yetiskinlerde fayda 6.000-8.000 adimda
///    plato yapar; gereksiz yuksek hedef verilmez).
/// Gun bittiginde o gunun hedefi DailyActivity.DailyGoal'da sabit kalir; profil degisirse yalnizca bugun etkilenir.
/// </summary>
public static class StepGoalCalculator
{
    public const string SourceAuto = "auto";

    public sealed record Result(int Goal, string Reason);

    public static Result Calculate(ActivityLevel? activityLevel, FitnessGoal? fitnessGoal, int? age, StepActivityOptions options)
    {
        var reasons = new List<string>();
        int goal;
        if (activityLevel is { } level)
        {
            goal = level switch
            {
                ActivityLevel.Sedentary => 6_000,
                ActivityLevel.LightlyActive => 7_500,
                ActivityLevel.ModeratelyActive => 9_000,
                ActivityLevel.VeryActive => 10_000,
                _ => 11_000,
            };
            reasons.Add("aktivite seviyene");
        }
        else
        {
            goal = options.DefaultDailyGoal;
        }

        if (fitnessGoal is { } g && FitnessGoals.Normalize(g) == FitnessGoal.LoseWeight)
        {
            goal += 1_500;
            reasons.Add("kilo verme hedefine");
        }

        if (age is >= 65)
        {
            goal = Math.Min(goal, 8_000);
            reasons.Add("yaşına");
        }
        else if (age is >= 60)
        {
            goal -= 1_000;
            reasons.Add("yaşına");
        }

        goal = (int)(Math.Round(goal / 500.0, MidpointRounding.AwayFromZero) * 500);
        goal = Math.Clamp(goal, options.AutoGoalMin, options.AutoGoalMax);

        var reason = reasons.Count == 0
            ? "Varsayılan günlük hedef. Profiline aktivite seviyeni eklediğinde hedefin buna göre ayarlanır."
            : $"Hedefin {Join(reasons)} göre otomatik belirlendi.";
        return new Result(goal, reason);
    }

    public static int AgeAt(DateTime dateOfBirth, DateOnly day)
    {
        var age = day.Year - dateOfBirth.Year;
        if (day < DateOnly.FromDateTime(dateOfBirth).AddYears(age))
        {
            age--;
        }

        return age;
    }

    private static string Join(List<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " ve " + parts[^1];
}
