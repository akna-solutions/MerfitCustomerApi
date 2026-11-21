using System.Globalization;
using MerfitCustomerApi.Business.Common;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>Bir egzersizin performans egilimi.</summary>
public enum PerformanceTrend
{
    /// <summary>Karar icin yeterli veri yok (en az 2 maruziyet gerekir).</summary>
    Insufficient,
    Improving,
    Stable,
    /// <summary>Son seansta dusus var ama onceki seans normaldi: tek seanslik dusus, program degismez.</summary>
    OneOffDip,
    /// <summary>Iki ardisik seansta dusus: kalici performans dususu.</summary>
    Declining
}

public static class ProgressionActions
{
    public const string Calibrate = "calibrate";
    public const string IncreaseWeight = "increase_weight";
    public const string ConsiderIncrease = "consider_increase";
    public const string KeepWeight = "keep_weight";
    public const string IncreaseReps = "increase_reps";
    public const string ReduceWeight = "reduce_weight";
    public const string HoldDiscomfort = "hold_discomfort";
}

/// <summary>Progresyon girdisi (planlanan recete + egzersiz bilgisi).</summary>
public sealed record ProgressionInput(
    int TargetSets,
    int RepsMin,
    int RepsMax,
    int? TargetRir,
    decimal IncrementKg,
    bool IsBodyweight,
    bool HasRecentDiscomfort);

/// <summary>Progresyon karari. <see cref="IsConfident"/> false ise karar kosulludur (or. RIR girilmedi).</summary>
public sealed record ProgressionDecision(
    string Action,
    decimal? PreviousWeightKg,
    decimal? SuggestedWeightKg,
    decimal? ChangeKg,
    int TargetRepsMin,
    int TargetRepsMax,
    bool IsConfident,
    PerformanceTrend Trend,
    string Message,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Cift progresyon (double progression) motoru. Saf ve deterministiktir.
///
/// Kurallar:
/// 1. Gecmis yoksa: kalibrasyon (kademeli yuk belirleme). Agirlik uretilmez.
/// 2. Son 3 haftada bu harekette agri / rahatsizlik bildirildiyse: artis yok, guvenli yonlendirme.
/// 3. Son maruziyette hedef set sayisinin tamami, her sette ust sinir tekrar, set tamamlanmis ve her sette
///    RIR >= hedef RIR ise: kucuk agirlik artisi (tek bir artis adimi).
///    RIR girilmemis set varsa: "kosullu artis" (kesin karar yok).
///    RIR hedefin altindaysa (beklenenden zor): ayni agirlikla devam.
/// 4. Tek seanslik tahmini 1RM dususu (%5+): program degismez. Iki ardisik dusus veya iki ardisik seansta alt
///    sinirin altinda kalma: yuk %7.5 azaltilir (artis adimina yuvarlanir).
/// 5. Vucut agirligi: ust sinira ulasildiysa tekrar artisi / zor varyasyon.
/// 6. Diger durumlar: ayni agirlikla tekrar artirmaya devam.
/// </summary>
public static class ProgressionEngine
{
    public static ProgressionDecision Decide(ProgressionInput input, IReadOnlyList<ExerciseExposure> exposures, TrainingEngineOptions options)
    {
        var min = Math.Max(1, input.RepsMin);
        var max = Math.Max(min, input.RepsMax);
        var ordered = exposures.Where(e => e.Sets.Any(s => !s.IsWarmup && s.Reps is > 0)).OrderBy(e => e.CompletedAt).ToList();
        var trend = Trend(ordered, options);
        var reasons = new List<string>();

        if (ordered.Count == 0)
        {
            return new ProgressionDecision(ProgressionActions.Calibrate, null, null, null, min, max, true, PerformanceTrend.Insufficient,
                input.IsBodyweight
                    ? $"İlk seans: {Range(min, max)} tekrarı temiz teknikle yapmaya odaklan."
                    : $"İlk seans kalibrasyon: hafif bir ağırlıkla başla, setler arasında küçük artışlarla {Range(min, max)} tekrarı {RirText(input.TargetRir)} yapabileceğin ağırlığı bul. Bu ağırlık sonraki seansların başlangıcı olacak.",
                new[] { "Bu egzersiz için kayıtlı set yok; başlangıç ağırlığı tahmin edilmedi." });
        }

        var last = ordered[^1];
        var working = last.Sets.Where(s => !s.IsWarmup && s.Reps is > 0).OrderBy(s => s.SetNumber).ToList();
        var workingWeight = LoadMath.WorkingWeight(last);

        if (input.HasRecentDiscomfort)
        {
            return new ProgressionDecision(ProgressionActions.HoldDiscomfort, workingWeight, workingWeight, 0m, min, max, true, trend,
                "Bu harekette rahatsızlık bildirdin. Ağırlığı artırma; ağrı yapmayan bir yükle veya alternatif bir hareketle devam et. Ağrı sürerse bir sağlık profesyoneline danış.",
                new[] { "Son günlerde bu egzersiz için rahatsızlık bildirimi var." });
        }

        var requiredSets = Math.Max(1, input.TargetSets);
        var allTop = working.Count >= requiredSets && working.All(s => s.Reps >= max) && working.All(s => s.IsCompleted);
        var targetRir = input.TargetRir ?? 1;
        var rirKnown = working.All(s => s.Rir.HasValue);
        var rirOk = rirKnown && working.All(s => s.Rir >= targetRir);

        if (input.IsBodyweight || workingWeight is null)
        {
            if (allTop && (rirOk || !rirKnown))
            {
                reasons.Add($"Son seansta tüm setlerde {max} tekrara ulaşıldı.");
                return new ProgressionDecision(ProgressionActions.IncreaseReps, null, null, null, max, max + 2, rirKnown, trend,
                    $"Tüm setlerde {max} tekrara ulaştın. Tekrarı artırabilir ya da daha zor bir varyasyona geçebilirsin.", reasons);
            }

            return new ProgressionDecision(ProgressionActions.KeepWeight, null, null, null, min, max, true, trend,
                $"Hedefin: tüm setlerde {max} tekrar.", new[] { "Üst sınıra henüz tüm setlerde ulaşılmadı." });
        }

        var increment = IncrementFor(workingWeight.Value, input.IncrementKg);

        // Kalici dusus: iki ardisik seans alt sinirin altinda ya da iki ardisik 1RM dususu.
        var belowMinTwice = ordered.Count >= 2 && ordered.TakeLast(2).All(e => e.Sets.Where(s => !s.IsWarmup && s.Reps is > 0).All(s => s.Reps < min));
        if (trend == PerformanceTrend.Declining || belowMinTwice)
        {
            var reduced = LoadMath.RoundDown(workingWeight.Value * (1 - options.DeclineLoadReductionPercent / 100m), increment > 0 ? increment : 0.5m);
            if (reduced >= workingWeight.Value) reduced = workingWeight.Value - (increment > 0 ? increment : 0.5m);
            reduced = Math.Max(0, reduced);
            reasons.Add(belowMinTwice
                ? $"Son iki seansta setler {min} tekrarın altında kaldı."
                : "Son iki seansta tahmini 1RM düştü (kalıcı düşüş).");
            return new ProgressionDecision(ProgressionActions.ReduceWeight, workingWeight, reduced, reduced - workingWeight.Value, min, max, true, trend,
                $"Son iki seansta performans düştü. Ağırlığı {Kg(reduced)} kg'a indirip {Range(min, max)} tekrarı sağlam yap; toparlanmanı da gözden geçir.", reasons);
        }

        if (trend == PerformanceTrend.OneOffDip)
        {
            reasons.Add("Son seansta düşüş var ama önceki seans normaldi; tek seanslık düşüşte program değiştirilmez.");
            return new ProgressionDecision(ProgressionActions.KeepWeight, workingWeight, workingWeight, 0m, min, max, true, trend,
                $"Geçen seans biraz düşük geçti, bu normal olabilir. {Kg(workingWeight.Value)} kg ile devam et.", reasons);
        }

        if (allTop && rirOk)
        {
            var suggested = workingWeight.Value + increment;
            reasons.Add($"Tüm {working.Count} sette {max} tekrara hedef RIR ({targetRir}) veya daha rahat ulaşıldı.");
            return new ProgressionDecision(ProgressionActions.IncreaseWeight, workingWeight, suggested, increment, min, max, true, trend,
                $"Tüm setlerde {max} tekrara kontrollü ulaştın. Bu seans {Kg(suggested)} kg ile {Range(min, max)} tekrar hedefle.", reasons);
        }

        if (allTop && !rirKnown)
        {
            var suggested = workingWeight.Value + increment;
            reasons.Add($"Tüm setlerde {max} tekrara ulaşıldı ama RIR girilmediği için artış kesinleştirilmedi.");
            return new ProgressionDecision(ProgressionActions.ConsiderIncrease, workingWeight, suggested, increment, min, max, false, trend,
                $"Tüm setlerde {max} tekrara ulaştın. Setlerde en az {targetRir} tekrar daha yapabilecek durumdaysan {Kg(suggested)} kg dene; zorlandıysan {Kg(workingWeight.Value)} kg ile devam et. Set sonunda RIR girersen öneri kesinleşir.",
                reasons);
        }

        if (allTop)
        {
            reasons.Add($"Üst sınıra ulaşıldı ama setler hedef RIR'dan ({targetRir}) daha zor geçti.");
            return new ProgressionDecision(ProgressionActions.KeepWeight, workingWeight, workingWeight, 0m, min, max, true, trend,
                $"{max} tekrara ulaştın ama setler zorladı. {Kg(workingWeight.Value)} kg ile aynı tekrarları daha rahat yapana kadar devam et.", reasons);
        }

        reasons.Add("Tüm setlerde üst sınıra henüz ulaşılmadı.");
        return new ProgressionDecision(ProgressionActions.KeepWeight, workingWeight, workingWeight, 0m, min, max, true, trend,
            $"{Kg(workingWeight.Value)} kg ile devam et; hedefin tüm setlerde {max} tekrar.", reasons);
    }

    /// <summary>
    /// Egilim: maruziyetlerin tahmini 1RM'leri (yoksa toplam tekrar) karsilastirilir. Son deger, onceki en fazla 3
    /// maruziyetin en iyisinden %X dusukse "dusus". Onceki maruziyet de kendi oncekilerine gore dususse "kalici dusus".
    /// </summary>
    public static PerformanceTrend Trend(IReadOnlyList<ExerciseExposure> ordered, TrainingEngineOptions options)
    {
        if (ordered.Count < 2)
        {
            return PerformanceTrend.Insufficient;
        }

        var scores = ordered.Select(Score).ToList();
        bool DropAt(int index)
        {
            if (index < 1) return false;
            var baseline = scores.Skip(Math.Max(0, index - 3)).Take(index - Math.Max(0, index - 3)).Max();
            return baseline > 0 && scores[index] < baseline * (1 - options.PerformanceDropPercent / 100m);
        }

        var lastIndex = scores.Count - 1;
        if (DropAt(lastIndex))
        {
            return DropAt(lastIndex - 1) ? PerformanceTrend.Declining : PerformanceTrend.OneOffDip;
        }

        var first = scores[Math.Max(0, scores.Count - 4)];
        return scores[lastIndex] > first ? PerformanceTrend.Improving : PerformanceTrend.Stable;
    }

    /// <summary>Maruziyet puani: tahmini 1RM; agirliksiz / 12+ tekrarli setlerde toplam tekrar x (agirlik veya 1).</summary>
    private static decimal Score(ExerciseExposure exposure) =>
        LoadMath.BestEstimate(exposure)
        ?? exposure.Sets.Where(s => !s.IsWarmup).Sum(s => (s.Reps ?? 0) * (s.WeightKg is > 0 ? s.WeightKg.Value : 1m));

    /// <summary>
    /// Artis: egzersizin (veya ekipman turunun) en kucuk artis adimi. Tek seferde bir adimdan fazla artirilmaz;
    /// boylece artis kucuk ve ekipmanda gercekten bulunan plaka / dambil adimina uygun olur.
    /// </summary>
    public static decimal IncrementFor(decimal workingWeight, decimal step) => step > 0 ? step : 0.5m;

    private static string Range(int min, int max) => min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}-{max}";

    private static string RirText(int? rir) => rir is null ? "kontrollü şekilde" : $"{rir} tekrar daha yapabilecek şekilde (RIR {rir})";

    public static string Kg(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
