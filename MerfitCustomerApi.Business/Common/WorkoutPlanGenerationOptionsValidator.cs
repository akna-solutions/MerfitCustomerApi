using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// <see cref="WorkoutPlanGenerationOptions"/> icindeki hedef agirlik profillerini dogrular. Eksik
/// veya hatali bir agirlik konfigurasyonu sessizce yanlis puanlama yapmak yerine uygulama
/// baslangicinda (ValidateOnStart) acik bir hatayla durdurulur. Yalnizca kullaniciya sunulan uc hedef
/// (<see cref="FitnessGoals.Primary"/>) zorunludur; eski hedefler plan uretiminden once bu uc hedeften
/// birine donusturulur, eski anahtarlar konfigurasyonda kalsa bile kullanilmaz.
/// </summary>
public class WorkoutPlanGenerationOptionsValidator : IValidateOptions<WorkoutPlanGenerationOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkoutPlanGenerationOptions options)
    {
        var errors = new List<string>();

        if (options.GoalFitMaxScore < 0)
        {
            errors.Add($"{nameof(WorkoutPlanGenerationOptions.GoalFitMaxScore)} negatif olamaz.");
        }

        if (options.PrimaryAttributeWeightsByGoal is null)
        {
            errors.Add($"{nameof(WorkoutPlanGenerationOptions.PrimaryAttributeWeightsByGoal)} tanimlanmalidir.");
            return ValidateOptionsResult.Fail(errors);
        }

        foreach (var goal in FitnessGoals.Primary)
        {
            if (!options.PrimaryAttributeWeightsByGoal.TryGetValue(goal, out var weights) || weights is null)
            {
                errors.Add($"{goal} hedefi icin antrenman ozellik agirliklari tanimlanmamis.");
                continue;
            }

            foreach (var attributeType in GoalAttributeWeights.PrimaryAttributes)
            {
                if (weights.GetWeight(attributeType) < 0)
                {
                    errors.Add($"{goal} hedefinin {attributeType} agirligi negatif olamaz.");
                }
            }

            if (weights.Total != GoalAttributeWeights.RequiredTotal)
            {
                errors.Add($"{goal} hedefinin agirlik toplami {GoalAttributeWeights.RequiredTotal} olmalidir (mevcut: {weights.Total}).");
            }
        }

        if (options.CardioSessionsPerWeekByGoal is not null)
        {
            foreach (var (goal, sessions) in options.CardioSessionsPerWeekByGoal)
            {
                if (sessions < 0 || sessions > 7)
                {
                    errors.Add($"{goal} hedefinin haftalik kardiyo sayisi 0-7 arasinda olmalidir (mevcut: {sessions}).");
                }
            }
        }

        if (options.CardioFinisherMinMinutes < 0 || options.CardioFinisherMaxMinutes < options.CardioFinisherMinMinutes)
        {
            errors.Add($"{nameof(WorkoutPlanGenerationOptions.CardioFinisherMaxMinutes)}, {nameof(WorkoutPlanGenerationOptions.CardioFinisherMinMinutes)} degerinden kucuk olamaz.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
