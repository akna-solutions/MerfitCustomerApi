using System.Globalization;
using MerfitCustomerApi.Business.Common;

namespace MerfitCustomerApi.Business.Services.Training;

public sealed record ExerciseProgress(
    long ExerciseId,
    string Name,
    MuscleRegion Region,
    int ExposureCount,
    decimal? FirstEstimatedOneRepMax,
    decimal? LastEstimatedOneRepMax,
    decimal? EstimatedOneRepMaxChange4Weeks,
    decimal? EstimatedOneRepMaxChange12Weeks,
    decimal? FirstTopWeightKg,
    decimal? LastTopWeightKg,
    int? LastBestReps,
    decimal TotalVolumeKg,
    PerformanceTrend Trend,
    IReadOnlyList<ExercisePoint> Points);

public sealed record ExercisePoint(DateTime Date, long SessionId, decimal? TopWeightKg, int? TopReps, decimal? EstimatedOneRepMax, decimal VolumeKg, int WorkingSets);

public sealed record RegionWeek(MuscleRegion Region, DateOnly WeekStart, decimal Sets, decimal VolumeKg);

public sealed record TrainingInsight(string Type, string Message, string? Region = null, long? ExerciseId = null);

public sealed record ProgressAnalysis(
    IReadOnlyList<ExerciseProgress> Exercises,
    IReadOnlyList<RegionWeek> RegionWeeks,
    int CompletedSessions4Weeks,
    int CompletedSessions12Weeks,
    double? CompletionRate4Weeks,
    IReadOnlyList<TrainingInsight> Insights,
    IReadOnlyList<string> DataNotes);

/// <summary>
/// Gelisim analizi. Yalnizca gercek set kayitlarindan hesaplar; yeterli veri yoksa yorum uretmez.
/// Tahmini 1RM (Epley) "tahmini" olarak etiketlenir. Kas grubu hacminde yardimci kaslar 0.5 set sayilir.
/// </summary>
public static class PerformanceAnalyzer
{
    public static ProgressAnalysis Analyze(
        IReadOnlyDictionary<long, IReadOnlyList<ExerciseExposure>> exposuresByExercise,
        IReadOnlyDictionary<long, CatalogExercise> catalog,
        TrainingHistory history,
        IReadOnlyDictionary<long, ProgressionDecision> readiness,
        TrainingEngineOptions options,
        DateTime now)
    {
        var notes = new List<string>();
        var insights = new List<TrainingInsight>();
        var since4 = now.AddDays(-28);
        var since12 = now.AddDays(-84);

        var progress = new List<ExerciseProgress>();
        foreach (var (exerciseId, all) in exposuresByExercise)
        {
            var exposures = all.Where(e => e.CompletedAt >= since12).OrderBy(e => e.CompletedAt).ToList();
            if (exposures.Count == 0) continue;
            var meta = catalog.GetValueOrDefault(exerciseId);
            var points = exposures.Select(e =>
            {
                var working = e.Sets.Where(s => !s.IsWarmup && s.Reps is > 0).ToList();
                var top = working.Where(s => s.WeightKg is > 0).OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).FirstOrDefault();
                return new ExercisePoint(e.CompletedAt, e.SessionId, top?.WeightKg, top?.Reps ?? working.Select(s => s.Reps).Max(),
                    LoadMath.BestEstimate(e), LoadMath.Volume(e), working.Count);
            }).ToList();

            var withEstimate = points.Where(p => p.EstimatedOneRepMax.HasValue).ToList();
            decimal? Change(DateTime since)
            {
                var window = withEstimate.Where(p => p.Date >= since).ToList();
                if (window.Count < 2) return null;
                var first = window[0].EstimatedOneRepMax!.Value;
                var last = window[^1].EstimatedOneRepMax!.Value;
                return first <= 0 ? null : Math.Round((last - first) / first * 100m, 1);
            }

            var trend = ProgressionEngine.Trend(exposures, options);
            progress.Add(new ExerciseProgress(
                exerciseId,
                meta?.Name ?? $"#{exerciseId}",
                meta?.PrimaryRegion ?? MuscleRegion.Other,
                exposures.Count,
                withEstimate.FirstOrDefault()?.EstimatedOneRepMax,
                withEstimate.LastOrDefault()?.EstimatedOneRepMax,
                Change(since4),
                Change(since12),
                points.FirstOrDefault(p => p.TopWeightKg.HasValue)?.TopWeightKg,
                points.LastOrDefault(p => p.TopWeightKg.HasValue)?.TopWeightKg,
                points[^1].TopReps,
                points.Sum(p => p.VolumeKg),
                trend,
                points));
        }

        // Kas grubu bazinda haftalik hacim (hafta Pazartesi baslar).
        var regionWeeks = new Dictionary<(MuscleRegion, DateOnly), (decimal Sets, decimal Volume)>();
        foreach (var (exerciseId, all) in exposuresByExercise)
        {
            var meta = catalog.GetValueOrDefault(exerciseId);
            if (meta is null) continue;
            foreach (var e in all.Where(x => x.CompletedAt >= since12))
            {
                var week = WeekStart(e.CompletedAt);
                var sets = e.Sets.Count(s => !s.IsWarmup && (s.Reps is > 0 || s.WeightKg is > 0));
                var volume = LoadMath.Volume(e);
                void Add(MuscleRegion region, decimal weight)
                {
                    var key = (region, week);
                    var current = regionWeeks.GetValueOrDefault(key);
                    regionWeeks[key] = (current.Sets + sets * weight, current.Volume + volume * weight);
                }
                Add(meta.PrimaryRegion, 1m);
                foreach (var secondary in meta.SecondaryRegions) Add(secondary, options.SecondaryMuscleSetWeight);
            }
        }
        var regionWeekList = regionWeeks
            .Select(kv => new RegionWeek(kv.Key.Item1, kv.Key.Item2, kv.Value.Sets, Math.Round(kv.Value.Volume, 1)))
            .OrderBy(r => r.WeekStart).ThenBy(r => r.Region)
            .ToList();

        var sessions = exposuresByExercise.Values.SelectMany(x => x).Select(e => (e.SessionId, e.CompletedAt)).Distinct().ToList();
        var completed4 = sessions.Count(s => s.CompletedAt >= since4);
        var completed12 = sessions.Count(s => s.CompletedAt >= since12);

        if (completed12 == 0)
        {
            notes.Add("Henüz tamamlanmış antrenman kaydı yok; gelişim yorumu üretilmedi.");
        }

        // 1) Egzersiz bazinda 4 haftalik gelisim / dusus.
        foreach (var p in progress.OrderByDescending(p => p.EstimatedOneRepMaxChange4Weeks ?? decimal.MinValue).ThenBy(p => p.ExerciseId))
        {
            if (p.EstimatedOneRepMaxChange4Weeks is decimal change && change >= 2.5m)
            {
                var window = p.Points.Where(x => x.Date >= since4 && x.EstimatedOneRepMax.HasValue).ToList();
                insights.Add(new TrainingInsight("exercise_improved",
                    $"{p.Name} performansın son 4 haftada gelişti (tahmini 1RM {Kg(window[0].EstimatedOneRepMax!.Value)} → {Kg(window[^1].EstimatedOneRepMax!.Value)} kg).",
                    p.Region.ToString(), p.ExerciseId));
            }
            else if (p.Trend == PerformanceTrend.Declining)
            {
                insights.Add(new TrainingInsight("exercise_declining",
                    $"{p.Name} hareketinde son iki seansta performans düştü.", p.Region.ToString(), p.ExerciseId));
            }
        }

        // 2) Kas grubu toplam hacim degisimi: son 2 hafta vs onceki 2 hafta (her iki donemde veri varsa).
        var thisWeek = WeekStart(now);
        foreach (var region in MuscleRegionMapper.Trainable)
        {
            decimal VolumeIn(int fromWeeksAgo, int toWeeksAgo) => regionWeekList
                .Where(r => r.Region == region && r.WeekStart <= thisWeek.AddDays(-7 * toWeeksAgo) && r.WeekStart > thisWeek.AddDays(-7 * fromWeeksAgo))
                .Sum(r => r.VolumeKg);
            var recent = VolumeIn(2, 0);
            var previous = VolumeIn(4, 2);
            if (recent > 0 && previous > 0)
            {
                var change = (recent - previous) / previous * 100m;
                if (change >= 10m)
                {
                    insights.Add(new TrainingInsight("region_volume_up",
                        $"{MuscleRegionMapper.Label(region)} antrenmanlarında toplam hacmin arttı (son 2 hafta, önceki 2 haftaya göre %{Math.Round(change, 0)}).",
                        region.ToString()));
                }
                else if (change <= -20m)
                {
                    insights.Add(new TrainingInsight("region_volume_down",
                        $"{MuscleRegionMapper.Label(region)} antrenmanlarında toplam hacmin azaldı (son 2 hafta, önceki 2 haftaya göre %{Math.Abs(Math.Round(change, 0))}).",
                        region.ToString()));
                }
            }
        }

        // 3) Son 2 haftada alt / ust vucut setlerini tamamlamakta zorlanma (basarisiz set orani).
        var since2 = now.AddDays(-14);
        foreach (var (label, isLower) in new[] { ("bacak", true), ("üst vücut", false) })
        {
            var sets = exposuresByExercise
                .Where(kv => catalog.TryGetValue(kv.Key, out var c) && MuscleRegionMapper.Trainable.Contains(c.PrimaryRegion)
                    && MuscleRegionMapper.IsLowerBody(c.PrimaryRegion) == isLower && c.PrimaryRegion != MuscleRegion.Core)
                .SelectMany(kv => kv.Value.Where(e => e.CompletedAt >= since2).SelectMany(e => e.Sets.Where(s => !s.IsWarmup)))
                .ToList();
            if (sets.Count >= 6)
            {
                var failed = sets.Count(s => !s.IsCompleted);
                if (failed * 10 >= sets.Count * 3)
                {
                    insights.Add(new TrainingInsight("completion_difficulty",
                        $"Son iki haftada {label} antrenmanlarını tamamlamakta zorlandın ({sets.Count} setin {failed}'i tamamlanamadı).",
                        isLower ? "Lower" : "Upper"));
                }
            }
        }

        // 4) Agirlik artisi icin uygun kosullar (progresyon karari "increase_weight" olan egzersizlerin bolgeleri).
        foreach (var group in readiness
            .Where(kv => kv.Value.Action == ProgressionActions.IncreaseWeight && catalog.ContainsKey(kv.Key))
            .GroupBy(kv => catalog[kv.Key].PrimaryRegion)
            .OrderBy(g => g.Key))
        {
            insights.Add(new TrainingInsight("ready_to_progress",
                $"{MuscleRegionMapper.Label(group.Key)} egzersizlerinde ağırlık artışı için uygun koşullar oluştu ({string.Join(", ", group.Select(kv => catalog[kv.Key].Name))}).",
                group.Key.ToString()));
        }

        // 5) Devamlilik.
        if (history.CompletionRate is double rate && history.ScheduledSessionsLast4Weeks >= 4)
        {
            if (rate < options.LowAdherenceThreshold)
            {
                insights.Add(new TrainingInsight("low_adherence",
                    $"Son 4 haftada planlanan {history.ScheduledSessionsLast4Weeks} antrenmanın {history.CompletedPlanSessionsLast4Weeks}'ini tamamladın. Daha kısa veya daha az günlük bir program daha sürdürülebilir olabilir."));
            }
        }
        else
        {
            notes.Add("Tamamlama oranı için yeterli planlı gün yok (en az 4 gerekir).");
        }

        notes.Add("Tahmini 1RM, Epley formülüyle (ağırlık × (1 + tekrar / 30)) hesaplanır; 12 tekrarın üzerindeki setler dahil edilmez. Gerçek maksimum testi değildir.");

        return new ProgressAnalysis(progress.OrderBy(p => p.Region).ThenBy(p => p.Name).ToList(), regionWeekList, completed4, completed12,
            history.ScheduledSessionsLast4Weeks >= 4 ? history.CompletionRate : null, insights, notes);
    }

    public static DateOnly WeekStart(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    }

    private static string Kg(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
