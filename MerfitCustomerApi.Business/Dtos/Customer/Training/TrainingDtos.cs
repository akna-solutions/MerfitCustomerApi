using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Training;

/// <summary>Antrenman motoru tercihleri. Tum alanlar istege baglidir.</summary>
public class TrainingPreferencesDto
{
    /// <summary>"Hypertrophy" | "Strength" | "FatLoss" | "GeneralFitness" | "Endurance". Bos ise profil hedefinden.</summary>
    public string? Focus { get; set; }

    /// <summary>Haftalik antrenman gunu (2-7). Bos ise profildeki gun sayisi.</summary>
    [Range(2, 7)]
    public int? DaysPerWeek { get; set; }

    /// <summary>Bir antrenmana ayrilabilecek sure (dk). Profildeki WorkoutDurationMin'e yazilir.</summary>
    [Range(15, 150)]
    public int? SessionMinutes { get; set; }

    [MaxLength(10)]
    public List<long> PriorityMuscleGroupIds { get; set; } = new();

    [MaxLength(50)]
    public List<long> PreferredExerciseIds { get; set; } = new();

    [MaxLength(50)]
    public List<long> AvoidedExerciseIds { get; set; } = new();

    /// <summary>Zorlanmak istenmeyen / kisitli kas gruplari (ana kasi bunlar olan hareketler secilmez).</summary>
    [MaxLength(10)]
    public List<long> LimitedMuscleGroupIds { get; set; } = new();

    /// <summary>Fiziksel kisitlama notu (motor yorumlamaz, teshis uretilmez).</summary>
    [StringLength(500)]
    public string? LimitationsNote { get; set; }

    /// <summary>
    /// Kullanilabilir ekipman (mevcut UserEquipment tablosu). Null = degistirme; bos liste = yalnizca vucut agirligi
    /// veya profildeki salon / ekipman bilgisi. Liste doluysa motor yalnizca bu ekipmanlarla yapilabilen egzersizleri secer.
    /// </summary>
    public List<long>? EquipmentIds { get; set; }

    /// <summary>Yanitta: "selected" (secili ekipman), "full" (salon / ekipmanli profil), "bodyweight".</summary>
    public string? EquipmentMode { get; set; }
}

/// <summary>Program olusturma istegi. Gonderilen tercihler kaydedilir.</summary>
public class GenerateTrainingProgramRequest : TrainingPreferencesDto
{
}

public class TrainingProgramDto
{
    public long Id { get; set; }
    public int Version { get; set; }

    /// <summary>"Proposed" | "Active" | "Archived" | "Rejected".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"Engine" | "Manual".</summary>
    public string Source { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Focus { get; set; }
    public string? SplitType { get; set; }
    public string? SplitLabel { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int BlockWeeks { get; set; }

    /// <summary>Aktif versiyonda kacinci haftada olundugu (1'den baslar).</summary>
    public int? CurrentWeek { get; set; }
    public bool IsDeload { get; set; }
    public long? PreviousPlanId { get; set; }
    public string? EngineVersion { get; set; }
    public List<TrainingDayDto> Days { get; set; } = new();
    public List<TrainingRegionVolumeDto> WeeklyVolume { get; set; } = new();

    /// <summary>Split secim gerekceleri.</summary>
    public List<string> SplitReasons { get; set; } = new();

    /// <summary>Motorun uyguladigi ayarlamalar (sure, devamlilik, oncelik...).</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>Eksik metadata uyarilari.</summary>
    public List<string> MetadataWarnings { get; set; } = new();

    /// <summary>Onceki versiyona gore neyin neden degistigi.</summary>
    public List<TrainingChangeDto> Changes { get; set; } = new();
}

public class TrainingProgramSummaryDto
{
    public long Id { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? SplitLabel { get; set; }
    public bool IsDeload { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Bu versiyonla tamamlanan antrenman sayisi (gecmis korunur).</summary>
    public int CompletedSessions { get; set; }
}

public class TrainingDayDto
{
    public long WorkoutPlanDayId { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Focus { get; set; }
    public int EstimatedMinutes { get; set; }
    public List<TrainingExerciseDto> Exercises { get; set; } = new();
}

public class TrainingExerciseDto
{
    public long PlanExerciseId { get; set; }
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public int Order { get; set; }
    public long? TargetMuscleGroupId { get; set; }
    public string? TargetMuscle { get; set; }

    /// <summary>"Compound" | "Isolation".</summary>
    public string? Mechanic { get; set; }
    public int Sets { get; set; }
    public int? RepsMin { get; set; }
    public int? RepsMax { get; set; }
    public int? DurationSeconds { get; set; }
    public int? TargetRir { get; set; }
    public string? TargetRirLabel { get; set; }
    public int? RestSeconds { get; set; }

    /// <summary>"FromHistory" | "RampUp" | "Bodyweight".</summary>
    public string? WeightGuidance { get; set; }
    public decimal? SuggestedWeightKg { get; set; }

    /// <summary>Baslangic agirligi icin kullaniciya gosterilecek talimat.</summary>
    public string? WeightInstruction { get; set; }
    public string? SelectionReason { get; set; }
    public List<TrainingAlternativeDto> Alternatives { get; set; } = new();
}

public class TrainingAlternativeDto
{
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Mechanic { get; set; }
    public string? ImageUrl { get; set; }
    public string? Reason { get; set; }
}

public class TrainingRegionVolumeDto
{
    public string Region { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal TargetSets { get; set; }
    public decimal DirectSets { get; set; }

    /// <summary>Bilesik hareketlerden gelen yardimci kas hacmi (her set 0.5 sayilir).</summary>
    public decimal IndirectSets { get; set; }
    public decimal TotalSets { get; set; }
}

public class TrainingChangeDto
{
    /// <summary>"created" | "split" | "exercise_added" | "exercise_removed" | "exercise_swapped" | "volume" | "deload" | "note".</summary>
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class SwapExerciseRequest
{
    [Range(1, long.MaxValue)]
    public long NewExerciseId { get; set; }
}

public class TrainingUpdateRecommendationDto
{
    public bool HasActiveProgram { get; set; }
    public bool IsUpdateRecommended { get; set; }
    public List<string> Reasons { get; set; } = new();
    public long? PendingProposalId { get; set; }
    public int? CurrentWeek { get; set; }
    public int? BlockWeeks { get; set; }
    public TrainingRecoveryDto? Recovery { get; set; }
}

public class TrainingRecoveryDto
{
    /// <summary>"Unknown" | "Good" | "Watch" | "DeloadRecommended".</summary>
    public string Status { get; set; } = string.Empty;
    public List<string> Signals { get; set; } = new();
    public List<TrainingChangeDto> Recommendations { get; set; } = new();
    public List<long> DiscomfortExerciseIds { get; set; } = new();
    public List<long> DecliningExerciseIds { get; set; } = new();
    public Dictionary<string, int?> DaysSinceLastTrained { get; set; } = new();
    public List<string> DataGaps { get; set; } = new();
}

public class TrainingProgressDto
{
    public int CompletedSessions4Weeks { get; set; }
    public int CompletedSessions12Weeks { get; set; }
    public double? CompletionRate4Weeks { get; set; }
    public List<TrainingExerciseProgressDto> Exercises { get; set; } = new();
    public List<TrainingRegionWeekDto> RegionWeeks { get; set; } = new();
    public List<TrainingInsightDto> Insights { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

public class TrainingExerciseProgressDto
{
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int ExposureCount { get; set; }

    /// <summary>Tahmini 1RM (Epley). Gercek maksimum testi degildir.</summary>
    public decimal? FirstEstimatedOneRepMax { get; set; }
    public decimal? LastEstimatedOneRepMax { get; set; }
    public decimal? EstimatedOneRepMaxChangePercent4Weeks { get; set; }
    public decimal? EstimatedOneRepMaxChangePercent12Weeks { get; set; }
    public decimal? FirstTopWeightKg { get; set; }
    public decimal? LastTopWeightKg { get; set; }
    public int? LastBestReps { get; set; }
    public decimal TotalVolumeKg { get; set; }
    public string Trend { get; set; } = string.Empty;
    public List<TrainingExercisePointDto> Points { get; set; } = new();
}

public class TrainingExercisePointDto
{
    public DateTime Date { get; set; }
    public long SessionId { get; set; }
    public decimal? TopWeightKg { get; set; }
    public int? TopReps { get; set; }
    public decimal? EstimatedOneRepMax { get; set; }
    public decimal VolumeKg { get; set; }
    public int WorkingSets { get; set; }
}

public class TrainingRegionWeekDto
{
    public string Region { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateOnly WeekStart { get; set; }
    public decimal Sets { get; set; }
    public decimal VolumeKg { get; set; }
}

public class TrainingInsightDto
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Region { get; set; }
    public long? ExerciseId { get; set; }
}

public class TrainingCoachRequest
{
    /// <summary>"program" | "progress" | "changes" | "alternative" | "question".</summary>
    [Required]
    [StringLength(20)]
    public string Topic { get; set; } = "program";

    /// <summary>"question" icin kullanicinin sorusu.</summary>
    [StringLength(500)]
    public string? Question { get; set; }

    /// <summary>"changes" icin onerilen / yeni versiyon; "alternative" icin plan egzersizi.</summary>
    public long? ProgramId { get; set; }
    public long? PlanExerciseId { get; set; }
}

public class TrainingCoachResponse
{
    public string Message { get; set; } = string.Empty;

    /// <summary>"ai" (AI yaniti dogrulandi) | "rules" (AI kullanilamadi veya yanit dogrulanmadi; kural tabanli metin).</summary>
    public string Source { get; set; } = "rules";

    /// <summary>Yanitta gecen ve veritabaninda dogrulanan egzersizler.</summary>
    public List<long> ReferencedExerciseIds { get; set; } = new();

    /// <summary>AI yaniti reddedildiyse nedeni (kullaniciya gosterilmez, tanilama).</summary>
    public string? ValidationNote { get; set; }
}
