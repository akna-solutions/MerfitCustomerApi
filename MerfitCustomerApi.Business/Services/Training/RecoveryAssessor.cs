using MerfitCustomerApi.Business.Common;

namespace MerfitCustomerApi.Business.Services.Training;

public enum RecoveryStatus
{
    /// <summary>Veri yetersiz.</summary>
    Unknown,
    Good,
    /// <summary>Dikkat: artislar durdurulmali / hacim hafifce azaltilmali.</summary>
    Watch,
    /// <summary>Asiri yuklenme belirtileri: deload onerilir.</summary>
    DeloadRecommended
}

public sealed record RecoveryRecommendation(string Code, string Message);

public sealed record RecoveryAssessment(
    RecoveryStatus Status,
    IReadOnlyList<string> Signals,
    IReadOnlyList<RecoveryRecommendation> Recommendations,
    IReadOnlyList<long> DiscomfortExerciseIds,
    IReadOnlyList<long> DecliningExerciseIds,
    IReadOnlyDictionary<MuscleRegion, int?> DaysSinceLastTrained,
    IReadOnlyList<string> DataGaps,
    ProgramAdjustments SuggestedAdjustments);

/// <summary>
/// Toparlanma degerlendirmesi. Programi yalnizca performansa gore degil; geri bildirim (yorgunluk, toparlanma,
/// zorluk), performans egilimi, devamlilik ve agri bildirimleriyle birlikte degerlendirir. Deload bir takvim
/// kurali degildir: yalnizca performans dususu VE yorgunluk / zayif toparlanma birlikte gorulurse onerilir.
/// Tibbi teshis uretmez.
/// </summary>
public static class RecoveryAssessor
{
    public static RecoveryAssessment Assess(
        TrainingHistory history,
        IReadOnlyDictionary<long, CatalogExercise> catalog,
        TrainingEngineOptions options,
        DateTime now)
    {
        var signals = new List<string>();
        var recommendations = new List<RecoveryRecommendation>();
        var gaps = new List<string> { "Uyku verisi uygulamada tutulmuyor; değerlendirmeye katılmadı." };

        var feedback = history.RecentFeedback.Where(f => f.CreatedAt >= now.AddDays(-14)).ToList();
        double? Avg(Func<FeedbackSignal, int?> pick)
        {
            var values = feedback.Select(pick).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            return values.Count >= 2 ? values.Average() : null;
        }

        var fatigue = Avg(f => f.Fatigue);
        var recovery = Avg(f => f.Recovery);
        var difficulty = Avg(f => f.Difficulty);
        if (feedback.Count < 2) gaps.Add("Son 2 haftada en az 2 antrenman değerlendirmesi yok; yorgunluk sinyali kullanılamadı.");

        var highFatigue = fatigue >= 4;
        var poorRecovery = recovery <= 2;
        if (highFatigue) signals.Add($"Son 2 haftada ortalama yorgunluk yüksek ({fatigue:0.0}/5).");
        if (poorRecovery) signals.Add($"Son 2 haftada kas toparlanması zayıf ({recovery:0.0}/5).");
        if (difficulty >= 4.5) signals.Add($"Antrenmanlar çok zor geçiyor ({difficulty:0.0}/5).");

        var tracked = history.ExposuresByExercise
            .Where(kv => kv.Value.Count >= 3 && kv.Value[^1].CompletedAt >= now.AddDays(-21))
            .ToList();
        var declining = tracked
            .Where(kv => ProgressionEngine.Trend(kv.Value.OrderBy(e => e.CompletedAt).ToList(), options) == PerformanceTrend.Declining)
            .Select(kv => kv.Key)
            .OrderBy(id => id)
            .ToList();
        var performanceDecline = declining.Count >= 2 && declining.Count * 10 >= tracked.Count * 3;
        if (performanceDecline)
        {
            signals.Add($"{tracked.Count} takip edilen hareketin {declining.Count}'inde üst üste performans düşüşü var.");
        }
        else if (tracked.Count < 2)
        {
            gaps.Add("Performans eğilimi için yeterli tekrar eden hareket kaydı yok (hareket başına en az 3 seans).");
        }

        var lowAdherence = history.CompletionRate is double rate && history.ScheduledSessionsLast4Weeks >= 4 && rate < options.LowAdherenceThreshold;
        if (lowAdherence) signals.Add($"Son 4 haftada antrenmanların %{(int)Math.Round(history.CompletionRate!.Value * 100)}'i tamamlandı.");

        var discomfort = history.RecentFeedback
            .Where(f => f.CreatedAt >= now.AddDays(-options.DiscomfortLookbackDays))
            .SelectMany(f => f.DiscomfortExerciseIds)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var status = RecoveryStatus.Good;
        var adjustments = ProgramAdjustments.None;
        var reasons = new List<string>();

        if (performanceDecline && (highFatigue || poorRecovery))
        {
            status = RecoveryStatus.DeloadRecommended;
            recommendations.Add(new("deload", "Performans düşüşü ve yüksek yorgunluk birlikte görülüyor. Bir haftalık deload öneriyoruz: setler azalır, ağırlıklar biraz düşer, hiçbir sette tükenişe gidilmez."));
            reasons.Add("Performans düşüşü ve yorgunluk birlikte görüldüğü için deload önerildi.");
            adjustments = adjustments with { IsDeload = true };
        }
        else if (performanceDecline || highFatigue || poorRecovery)
        {
            status = RecoveryStatus.Watch;
            recommendations.Add(new("pause_increases", "Ağırlık artışlarını bir süre durdur; mevcut ağırlıklarla tekrarları sağlamlaştır."));
            recommendations.Add(new("reduce_sets", "Haftalık set sayısı yaklaşık %15 azaltılabilir."));
            recommendations.Add(new("increase_rest", "Setler arası dinlenmeyi 30 sn artır."));
            reasons.Add("Yorgunluk veya performans düşüşü sinyali nedeniyle hacim azaltıldı ve dinlenme uzatıldı.");
            adjustments = adjustments with { VolumeFactor = options.HighFatigueVolumeFactor, ExtraRestSeconds = 30 };
        }
        else if (feedback.Count < 2 && tracked.Count < 2)
        {
            status = RecoveryStatus.Unknown;
        }

        if (lowAdherence)
        {
            recommendations.Add(new("adherence", "Antrenmanları tamamlamakta zorlanıyorsan daha kısa seanslar veya haftada bir gün daha az daha sürdürülebilir olabilir. Kaçırılan antrenmanlar performans düşüşü sayılmaz."));
        }

        if (discomfort.Count > 0)
        {
            var names = discomfort.Select(id => catalog.TryGetValue(id, out var c) ? c.Name : $"#{id}");
            recommendations.Add(new("discomfort", $"Rahatsızlık bildirdiğin hareketler ({string.Join(", ", names)}) zorlanmayacak; yeni versiyonda veritabanındaki alternatifleriyle değiştirilir. Ağrı sürerse veya şiddetliyse bir sağlık profesyoneline danış."));
            reasons.Add("Rahatsızlık bildirilen hareketler alternatifleriyle değiştirildi.");
            adjustments = adjustments with { ExcludedExerciseIds = discomfort.ToHashSet() };
        }

        adjustments = adjustments with { Reasons = reasons };

        // Kas grubu bazinda son antrenmandan beri gecen gun.
        var daysSince = MuscleRegionMapper.Trainable.ToDictionary(r => r, r =>
        {
            var last = history.ExposuresByExercise
                .Where(kv => catalog.TryGetValue(kv.Key, out var c) && (c.PrimaryRegion == r || c.SecondaryRegions.Contains(r)))
                .SelectMany(kv => kv.Value)
                .Select(e => (DateTime?)e.CompletedAt)
                .Max();
            return last is null ? (int?)null : (int)(now.Date - last.Value.Date).TotalDays;
        });

        return new RecoveryAssessment(status, signals, recommendations, discomfort, declining, daysSince, gaps, adjustments);
    }
}
