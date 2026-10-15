using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;

namespace MerfitCustomerApi.Business.Services.WorkoutSessions;

/// <summary>
/// Saf (veritabanindan bagimsiz) cift progresyon kurali. Girdi: egzersizin hedef set sayisi, tekrar
/// araligi ve kullanicinin bu egzersizi en son TAMAMLADIGI antrenmandaki setleri.
///
/// - Hedef set sayisinin tamami loglanmis, her sette tekrar araliginin ust sinirina ulasilmis ve (RPE
///   girildiyse) setler tukenise cok yakin degilse: kucuk bir agirlik artisi onerilir ve tekrar
///   hedefi araligin alt sinirina doner.
/// - Aksi halde agirlik korunur, hedef her sette ust sinira ulasmaktir.
/// - Vucut agirligi egzersizinde (agirlik yok) ust sinira ulasildiginda tekrar artisi onerilir.
/// - Sureye dayali veya tekrar hedefi olmayan egzersizlerde oneri uretilmez.
/// </summary>
public static class ProgressiveOverloadAdvisor
{
    public const string ActionStart = "start";
    public const string ActionIncreaseWeight = "increase_weight";
    public const string ActionKeepWeight = "keep_weight";
    public const string ActionIncreaseReps = "increase_reps";

    public static CustomerProgressionSuggestionDto? Suggest(
        int targetSets,
        int? repsMin,
        int? repsMax,
        IReadOnlyList<CustomerWorkoutSetLogDto>? lastSets,
        ProgressiveOverloadOptions options)
    {
        if (repsMin is not > 0)
        {
            return null;
        }

        var min = repsMin.Value;
        var max = Math.Max(min, repsMax ?? min);
        var repSets = lastSets?.Where(s => s.Reps is > 0).OrderBy(s => s.SetNumber).ToList() ?? new();

        if (repSets.Count == 0)
        {
            return new CustomerProgressionSuggestionDto
            {
                Action = ActionStart,
                TargetRepsMin = min,
                TargetRepsMax = max,
                Message = $"{RangeText(min, max)} tekrar yapabileceğin bir ağırlıkla başla.",
            };
        }

        var requiredSets = Math.Max(1, targetSets);
        var weights = repSets.Where(s => s.WeightKg is > 0).Select(s => s.WeightKg!.Value).ToList();
        var isWeighted = weights.Count > 0;
        var workingWeight = isWeighted ? weights.Min() : (decimal?)null;

        var reachedTop = repSets.Count >= requiredSets
            && repSets.All(s => s.Reps >= max)
            && (!isWeighted || weights.Count == repSets.Count)
            && repSets.All(s => s.Rpe is null || s.Rpe <= options.MaxRpeForIncrease);

        if (!isWeighted)
        {
            return reachedTop
                ? new CustomerProgressionSuggestionDto
                {
                    Action = ActionIncreaseReps,
                    TargetRepsMin = max,
                    TargetRepsMax = max + 2,
                    Message = $"Geçen sefer tüm setlerde {max} tekrara ulaştın. Tekrarı artırabilir ya da daha zor bir varyasyon deneyebilirsin.",
                }
                : new CustomerProgressionSuggestionDto
                {
                    Action = ActionKeepWeight,
                    TargetRepsMin = min,
                    TargetRepsMax = max,
                    Message = $"Hedefin: tüm setlerde {max} tekrar.",
                };
        }

        if (reachedTop)
        {
            var increment = IncreaseFor(workingWeight!.Value, options);
            var suggested = workingWeight.Value + increment;
            return new CustomerProgressionSuggestionDto
            {
                Action = ActionIncreaseWeight,
                PreviousWeightKg = workingWeight,
                SuggestedWeightKg = suggested,
                IncreaseKg = increment,
                TargetRepsMin = min,
                TargetRepsMax = max,
                Message = $"Geçen sefer tüm setlerde {max} tekrara ulaştın. Bu sefer {Kg(suggested)} kg ile {RangeText(min, max)} tekrar hedefle.",
            };
        }

        return new CustomerProgressionSuggestionDto
        {
            Action = ActionKeepWeight,
            PreviousWeightKg = workingWeight,
            SuggestedWeightKg = workingWeight,
            TargetRepsMin = min,
            TargetRepsMax = max,
            Message = $"{Kg(workingWeight!.Value)} kg ile devam et; hedefin tüm setlerde {max} tekrar.",
        };
    }

    /// <summary>Yuzdesel artis, [Min, Max] araligina sinirlanip adima yuvarlanir.</summary>
    public static decimal IncreaseFor(decimal workingWeightKg, ProgressiveOverloadOptions options)
    {
        var raw = workingWeightKg * options.IncreasePercent / 100m;
        var clamped = Math.Clamp(raw, options.MinIncreaseKg, Math.Max(options.MinIncreaseKg, options.MaxIncreaseKg));
        var step = options.RoundingStepKg > 0 ? options.RoundingStepKg : 0.5m;
        var rounded = Math.Round(clamped / step, MidpointRounding.AwayFromZero) * step;
        return Math.Max(step, rounded);
    }

    private static string RangeText(int min, int max) => min == max ? min.ToString() : $"{min}-{max}";

    private static string Kg(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
