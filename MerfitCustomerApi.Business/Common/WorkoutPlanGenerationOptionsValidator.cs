using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// <see cref="WorkoutPlanGenerationOptions"/> icindeki hedef agirlik profillerini dogrular. Eksik
/// veya hatali bir agirlik konfigurasyonu sessizce yanlis puanlama yapmak yerine uygulama
/// baslangicinda (ValidateOnStart) acik bir hatayla durdurulur.
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

        if (options.AttributeWeightsByGoal is null)
        {
            errors.Add($"{nameof(WorkoutPlanGenerationOptions.AttributeWeightsByGoal)} tanimlanmalidir.");
            return ValidateOptionsResult.Fail(errors);
        }

        foreach (var goal in Enum.GetValues<FitnessGoal>())
        {
            if (!options.AttributeWeightsByGoal.TryGetValue(goal, out var weights) || weights is null)
            {
                errors.Add($"{goal} hedefi icin antrenman ozellik agirliklari tanimlanmamis.");
                continue;
            }

            foreach (var attributeType in Enum.GetValues<WorkoutAttributeType>())
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

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
