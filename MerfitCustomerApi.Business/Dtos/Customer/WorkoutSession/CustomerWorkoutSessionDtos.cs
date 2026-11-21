using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;

/// <summary>POST /api/workout-sessions govdesi; bir antrenmani baslatmak icin sadece hangi Workout'un secildigi gerekir.</summary>
public class StartWorkoutSessionRequest
{
    /// <summary>
    /// Katalog antrenmaninin kimligi. Antrenman motorunun plan gununden baslatirken bos birakilabilir
    /// (WorkoutPlanDayId zorunlu olur).
    /// </summary>
    public long? WorkoutId { get; set; }

    /// <summary>
    /// Opsiyonel. Oturum, kullanicinin kisisel programindaki ("Bugünün Antrenmanı") bir
    /// gunden baslatiliyorsa bu alan doldurulmalidir; boylece hedef set/tekrar/dinlenme
    /// degerleri genel katalog yerine WorkoutPlanExercise'daki kisisel degerlerden okunur.
    /// Genel antrenman kataloğundan (Workout Catalog) baslatilan oturumlarda bos birakilmalidir.
    /// </summary>
    public long? WorkoutPlanDayId { get; set; }
}

/// <summary>
/// Aktif oturumun tam gorunumu. MerfitNativeApp workout/types.ts::WorkoutSession + Exercise[]
/// verisinin API karsiligi; ActiveWorkout ekrani bu DTO'yu direkt render edebilir.
/// </summary>
public class CustomerWorkoutSessionDto
{
    public long Id { get; set; }
    public long? WorkoutId { get; set; }

    /// <summary>Oturum bir plan gununden baslatildiysa o gunun kimligi.</summary>
    public long? WorkoutPlanDayId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>"Started" | "Paused" | "Completed" | "Cancelled" | "Abandoned".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }

    public List<CustomerWorkoutSessionExerciseDto> Exercises { get; set; } = new();
}

public class CustomerWorkoutSessionExerciseDto
{
    /// <summary>WorkoutSessionExercise.Id - set loglarken bu kimlik kullanilir.</summary>
    public long SessionExerciseId { get; set; }
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Hedef set sayisi (antrenman planindan).</summary>
    public int TargetSets { get; set; }

    /// <summary>Hedef tekrar (kisisel planda tekrar araliginin alt siniri).</summary>
    public int? TargetReps { get; set; }

    /// <summary>Tekrar araliginin ust siniri (kisisel plan); aralik yoksa null.</summary>
    public int? TargetRepsMax { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Bu oturumda simdiye kadar loglanan setler.</summary>
    public List<CustomerWorkoutSetLogDto> CompletedSets { get; set; } = new();

    /// <summary>Kullanicinin bu egzersizdeki onceki en iyi performansi ("YENI REKOR" rozeti icin); hic yoksa null.</summary>
    public CustomerPreviousBestDto? PreviousBest { get; set; }

    /// <summary>
    /// Egzersiz harici agirlik gerektiriyorsa (Exercise.EquipmentType bodyweight degilse) true. True iken
    /// 0 / bos agirlik kabul edilmez; false iken (vucut agirligi egzersizi) agirlik opsiyoneldir.
    /// </summary>
    public bool RequiresWeight { get; set; }

    /// <summary>Kullanicinin bu egzersizi en son tamamladigi oturumdaki setleri ("Geçen sefer"); hic yoksa null.</summary>
    public CustomerLastPerformanceDto? LastPerformance { get; set; }

    /// <summary>
    /// Cift progresyon onerisi (bir sonraki hedef): son tamamlanan antrenmana gore agirlik artisi veya
    /// ayni agirlikla ust tekrar hedefi. Sureye dayali egzersizlerde null.
    /// </summary>
    public CustomerProgressionSuggestionDto? Progression { get; set; }

    /// <summary>Hedef RIR (yalnizca antrenman motoru planlarinda).</summary>
    public int? TargetRir { get; set; }

    /// <summary>"FromHistory" | "RampUp" | "Bodyweight" (yalnizca motor planlarinda).</summary>
    public string? WeightGuidance { get; set; }

    /// <summary>Program olusturulurken gecmisten hesaplanan baslangic agirligi (yoksa bos; uydurma deger yok).</summary>
    public decimal? SuggestedStartWeightKg { get; set; }
}

/// <summary>Bir egzersiz icin bir sonraki hedef.</summary>
public class CustomerProgressionSuggestionDto
{
    /// <summary>"start" | "increase_weight" | "keep_weight" | "increase_reps".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Son antrenmandaki calisma agirligi (kg).</summary>
    public decimal? PreviousWeightKg { get; set; }

    /// <summary>Onerilen agirlik (kg); vucut agirligi egzersizinde null.</summary>
    public decimal? SuggestedWeightKg { get; set; }

    /// <summary>Onerilen artis (kg); yalnizca increase_weight.</summary>
    public decimal? IncreaseKg { get; set; }

    public int TargetRepsMin { get; set; }
    public int TargetRepsMax { get; set; }

    /// <summary>Kullaniciya gosterilecek kisa metin.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Hedef RIR (motor planlarinda).</summary>
    public int? TargetRir { get; set; }

    /// <summary>false ise oneri kosulludur (or. RIR girilmedigi icin artis kesinlestirilmedi).</summary>
    public bool IsConfident { get; set; } = true;

    /// <summary>"Insufficient" | "Improving" | "Stable" | "OneOffDip" | "Declining" (motor planlarinda).</summary>
    public string? Trend { get; set; }

    /// <summary>Kararin kural tabanli gerekceleri.</summary>
    public List<string> Reasons { get; set; } = new();
}

public class CustomerLastPerformanceDto
{
    public long SessionId { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<CustomerWorkoutSetLogDto> Sets { get; set; } = new();
}

public class CustomerWorkoutSetLogDto
{
    public int SetNumber { get; set; }
    public decimal? WeightKg { get; set; }
    public int? Reps { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? Rpe { get; set; }

    /// <summary>Sette kalan tekrar (0 = tukenis).</summary>
    public int? Rir { get; set; }

    public bool IsWarmup { get; set; }

    public bool IsCompleted { get; set; } = true;
}

public class CustomerPreviousBestDto
{
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
}

/// <summary>POST /api/workout-sessions/{id}/exercises/{sessionExerciseId}/sets govdesi.</summary>
public class LogWorkoutSetRequest
{
    [Range(1, 50, ErrorMessage = "SetNumber 1-50 araliginda olmalidir.")]
    public int SetNumber { get; set; }

    [Range(0, 1000, ErrorMessage = "WeightKg gecerli bir deger olmalidir.")]
    public decimal? WeightKg { get; set; }

    [Range(1, 1000, ErrorMessage = "Tekrar sayisi en az 1 olmalidir.")]
    public int? Reps { get; set; }

    [Range(0, 36000, ErrorMessage = "DurationSeconds gecerli bir deger olmalidir.")]
    public int? DurationSeconds { get; set; }

    [Range(0, 10, ErrorMessage = "Rpe 0-10 araliginda olmalidir.")]
    public decimal? Rpe { get; set; }

    /// <summary>
    /// Sette kalan tekrar: 4+ rahat, 3 orta, 2 zorlayici ama kontrollu, 1 cok zor, 0 tukenis. Girilmezse ve RPE
    /// varsa RIR = 10 - RPE. Ikisi de yoksa motor agirlik artisi icin kesin karar vermez.
    /// </summary>
    [Range(0, 10, ErrorMessage = "Rir 0-10 araliginda olmalidir.")]
    public int? Rir { get; set; }

    /// <summary>Isinma seti mi? (varsayilan false). Isinma setleri hacim ve progresyona katilmaz.</summary>
    public bool? IsWarmup { get; set; }

    /// <summary>Set planlandigi gibi tamamlandi mi? (varsayilan true). false = basarisiz / yarim set.</summary>
    public bool? IsCompleted { get; set; }
}

/// <summary>PUT /api/workout-sessions/{id}/complete govdesi.</summary>
public class CompleteWorkoutSessionRequest
{
    [Range(1, 36000, ErrorMessage = "DurationSeconds gecerli bir deger olmalidir.")]
    public int DurationSeconds { get; set; }

    [Range(0, 10000, ErrorMessage = "CaloriesBurned gecerli bir deger olmalidir.")]
    public decimal? CaloriesBurned { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>
/// Tamamlanan oturumun ozeti. MerfitNativeApp WorkoutSummary ekraninin ihtiyac duydugu
/// "yeni rekor" bilgisini de icerir.
/// </summary>
public class CustomerWorkoutSessionSummaryDto
{
    public long SessionId { get; set; }
    public int DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public List<CustomerNewPersonalRecordDto> NewPersonalRecords { get; set; } = new();
}

public class CustomerNewPersonalRecordDto
{
    public long ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
}

/// <summary>
/// GET /api/workout-sessions (gecmis) listesindeki tek bir tamamlanmis oturum. Detay (egzersiz ve
/// set bazinda kilo/tekrar) icin GET /api/workout-sessions/{id} kullanilir.
/// </summary>
public class CustomerWorkoutSessionHistoryItemDto
{
    public long Id { get; set; }
    public long? WorkoutId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public int ExerciseCount { get; set; }
    public int SetCount { get; set; }

    /// <summary>Toplam hacim: tum setlerde agirlik x tekrar toplami (kg).</summary>
    public decimal TotalVolumeKg { get; set; }

    /// <summary>Oturumda kaldirilan en yuksek agirlik; agirlikli set yoksa null.</summary>
    public decimal? MaxWeightKg { get; set; }
}

/// <summary>
/// Antrenman sonu kisa degerlendirme. Tum alanlar istege baglidir (1-5).
/// </summary>
public class SubmitWorkoutFeedbackRequest
{
    /// <summary>Antrenman zorlugu: 1 cok kolay ... 5 cok zor.</summary>
    [Range(1, 5)]
    public int? Difficulty { get; set; }

    /// <summary>Genel yorgunluk: 1 dinc ... 5 cok yorgun.</summary>
    [Range(1, 5)]
    public int? Fatigue { get; set; }

    /// <summary>Kaslarin toparlanmasi: 1 hic ... 5 tamamen.</summary>
    [Range(1, 5)]
    public int? Recovery { get; set; }

    /// <summary>Programdan memnuniyet: 1 hic ... 5 cok.</summary>
    [Range(1, 5)]
    public int? Satisfaction { get; set; }

    public bool HasDiscomfort { get; set; }

    /// <summary>Rahatsizlik veren egzersizler (bu oturumdaki egzersizlerden).</summary>
    [MaxLength(20)]
    public List<long>? DiscomfortExerciseIds { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class CustomerWorkoutFeedbackDto
{
    public long SessionId { get; set; }
    public int? Difficulty { get; set; }
    public int? Fatigue { get; set; }
    public int? Recovery { get; set; }
    public int? Satisfaction { get; set; }
    public bool HasDiscomfort { get; set; }
    public List<long> DiscomfortExerciseIds { get; set; } = new();
    public string? Note { get; set; }

    /// <summary>Rahatsizlik bildirildiyse guvenli yonlendirme (tibbi teshis degildir).</summary>
    public string? SafetyMessage { get; set; }
}
