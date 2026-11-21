namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Tahmini 1RM ve yuk hesaplari. Formul: Epley, 1RM = agirlik x (1 + tekrar / 30). Uygulamadaki rekor
/// hesabiyla (PersonalRecord) aynidir. Bu bir TAHMINDIR, gercek maksimum testi degildir; 12 tekrarin
/// ustundeki setlerde guvenilirligi duser, bu yuzden o setler 1RM tahminine katilmaz.
/// </summary>
public static class LoadMath
{
    public const int MaxRepsForEstimate = 12;

    public static decimal? EstimateOneRepMax(decimal? weightKg, int? reps)
    {
        if (weightKg is not > 0 || reps is not > 0 || reps > MaxRepsForEstimate)
        {
            return null;
        }

        return reps == 1 ? weightKg.Value : Math.Round(weightKg.Value * (1 + reps.Value / 30m), 2);
    }

    /// <summary>Bir maruziyetteki en iyi calisma setinin tahmini 1RM'i.</summary>
    public static decimal? BestEstimate(ExerciseExposure exposure)
    {
        var estimates = exposure.Sets
            .Where(s => !s.IsWarmup)
            .Select(s => EstimateOneRepMax(s.WeightKg, s.Reps))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        return estimates.Count == 0 ? null : estimates.Max();
    }

    /// <summary>Calisma agirligi: tamamlanmis agirlikli calisma setlerinin en dusugu (temkinli).</summary>
    public static decimal? WorkingWeight(ExerciseExposure exposure)
    {
        var weights = exposure.Sets.Where(s => !s.IsWarmup && s.WeightKg is > 0 && s.Reps is > 0).Select(s => s.WeightKg!.Value).ToList();
        return weights.Count == 0 ? null : weights.Min();
    }

    /// <summary>Toplam hacim (kg x tekrar), isinma haric.</summary>
    public static decimal Volume(ExerciseExposure exposure) => exposure.Sets
        .Where(s => !s.IsWarmup)
        .Sum(s => (s.WeightKg ?? 0) * (s.Reps ?? 0));

    public static decimal RoundDown(decimal value, decimal step)
    {
        var s = step > 0 ? step : 0.5m;
        return Math.Floor(value / s) * s;
    }

    public static decimal RoundNearest(decimal value, decimal step)
    {
        var s = step > 0 ? step : 0.5m;
        return Math.Round(value / s, MidpointRounding.AwayFromZero) * s;
    }

    /// <summary>
    /// Gecmisten calisma agirligi onerisi. Son 12 haftadaki en son agirlikli maruziyet kullanilir:
    /// tekrarlar yeni araliga yakinsa (alt sinir ile ust sinir + 2 arasi) ayni agirlik; degilse tahmini 1RM'den
    /// alt sinir + hedef RIR'a gore hesaplanip artis adimina asagi yuvarlanir. Gecmis yoksa null (uydurma agirlik yok).
    /// </summary>
    public static decimal? SuggestFromHistory(
        IReadOnlyList<ExerciseExposure> exposures, int repsMin, int repsMax, int targetRir, decimal increment, DateTime now)
    {
        var recent = exposures
            .Where(e => e.CompletedAt >= now.AddDays(-84) && WorkingWeight(e).HasValue)
            .OrderByDescending(e => e.CompletedAt)
            .FirstOrDefault();
        if (recent is null)
        {
            return null;
        }

        var working = WorkingWeight(recent)!.Value;
        var reps = recent.Sets.Where(s => !s.IsWarmup && s.WeightKg is > 0 && s.Reps is > 0).Select(s => s.Reps!.Value).ToList();
        var avgReps = reps.Average();
        if (avgReps >= repsMin && avgReps <= repsMax + 2)
        {
            return working;
        }

        var best = recent.Sets
            .Where(s => !s.IsWarmup && s.WeightKg is > 0 && s.Reps is > 0)
            .Select(s => s.WeightKg!.Value * (1 + (s.Reps!.Value + (s.Rir ?? 0)) / 30m))
            .Max();
        var target = best / (1 + (repsMin + targetRir) / 30m);
        var rounded = RoundDown(target, increment);
        return rounded > 0 ? rounded : null;
    }
}
