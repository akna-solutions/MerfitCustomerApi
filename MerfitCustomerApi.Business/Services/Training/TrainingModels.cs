using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Motorun kas grubu adlarini esledigi kanonik bolgeler. Veritabanindaki MuscleGroup satirlari ad/slug
/// anahtar kelimeleriyle (veya TrainingEngineOptions.MuscleGroupAliases ile) bu bolgelere eslenir.
/// </summary>
public enum MuscleRegion
{
    Chest,
    Back,
    Shoulders,
    Biceps,
    Triceps,
    Quadriceps,
    Hamstrings,
    Glutes,
    Calves,
    Core,
    Forearms,
    FullBody,
    Cardio,
    Other
}

/// <summary>Egzersizin yuk turu; agirlik artis adimini belirler.</summary>
public enum LoadType
{
    Barbell,
    Dumbbell,
    Machine,
    Cable,
    Kettlebell,
    Bodyweight,
    Band,
    Other
}

/// <summary>
/// Motorun bir katalog egzersizi hakkinda bildigi her sey. <see cref="MechanicInferred"/> ve
/// <see cref="EquipmentInferred"/> metadata eksik oldugunda tahmin yapildigini gosterir.
/// </summary>
public sealed record CatalogExercise
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DifficultyLevel Difficulty { get; init; }
    public long PrimaryMuscleGroupId { get; init; }
    public MuscleRegion PrimaryRegion { get; init; }
    public IReadOnlyList<MuscleRegion> SecondaryRegions { get; init; } = Array.Empty<MuscleRegion>();
    public ExerciseMechanicType Mechanic { get; init; }
    public bool MechanicInferred { get; init; }
    public MovementPattern? Pattern { get; init; }
    public bool PatternInferred { get; init; }
    public LoadType LoadType { get; init; }
    public decimal LoadIncrementKg { get; init; }

    /// <summary>Gereken ekipman kimlikleri (bos = ekipmansiz).</summary>
    public IReadOnlyList<long> RequiredEquipmentIds { get; init; } = Array.Empty<long>();

    /// <summary>EquipmentType metni var ama hicbir Equipment kaydiyla eslesmedi.</summary>
    public bool HasUnresolvedEquipment { get; init; }
    public bool EquipmentInferred { get; init; }
    public bool IsBodyweight { get; init; }
    public bool IsTimeBased { get; init; }
}

/// <summary>Bir egzersizin tek bir tamamlanmis oturumdaki calisma setleri (isinma setleri haric).</summary>
public sealed record ExerciseExposure(
    long ExerciseId,
    long SessionId,
    DateTime CompletedAt,
    IReadOnlyList<PerformedSet> Sets);

/// <summary>Kaydedilmis tek set. <see cref="Rir"/> bos ise RIR / RPE girilmemistir.</summary>
public sealed record PerformedSet(int SetNumber, decimal? WeightKg, int? Reps, int? Rir, bool IsCompleted, bool IsWarmup = false);

/// <summary>Antrenman sonu geri bildirimi (motor icin).</summary>
public sealed record FeedbackSignal(
    long SessionId,
    DateTime CreatedAt,
    int? Difficulty,
    int? Fatigue,
    int? Recovery,
    int? Satisfaction,
    bool HasDiscomfort,
    IReadOnlyList<long> DiscomfortExerciseIds);

/// <summary>
/// Program uretimi icin kullanici girdileri (profil + tercihler + istek ustune yazmalari).
/// </summary>
public sealed record TrainingProfile
{
    public long UserId { get; init; }
    public int? Age { get; init; }
    public Gender? Gender { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? WeightKg { get; init; }
    public TrainingFocus Focus { get; init; } = TrainingFocus.Hypertrophy;
    public bool FocusFromProfileGoal { get; init; }
    public ExperienceLevel Experience { get; init; } = ExperienceLevel.Beginner;
    public bool ExperienceDefaulted { get; init; }
    public int RequestedDaysPerWeek { get; init; } = 3;
    public IReadOnlyList<DayOfWeek> AvailableDays { get; init; } = Array.Empty<DayOfWeek>();
    public int SessionMinutes { get; init; } = 45;
    public bool SessionMinutesDefaulted { get; init; }

    /// <summary>Kullanicinin sahip oldugu ekipman kimlikleri.</summary>
    public IReadOnlySet<long> EquipmentIds { get; init; } = new HashSet<long>();

    /// <summary>Spor salonu kullanicisi: eslenemeyen ekipman metinli egzersizler de secilebilir.</summary>
    public bool HasGymAccess { get; init; }
    public IReadOnlySet<MuscleRegion> PriorityRegions { get; init; } = new HashSet<MuscleRegion>();
    public IReadOnlySet<long> PreferredExerciseIds { get; init; } = new HashSet<long>();
    public IReadOnlySet<long> AvoidedExerciseIds { get; init; } = new HashSet<long>();
    public IReadOnlySet<MuscleRegion> LimitedRegions { get; init; } = new HashSet<MuscleRegion>();
    public string? LimitationsNote { get; init; }
}

/// <summary>Kullanicinin gecmisi: performans, geri bildirim ve devamlilik.</summary>
public sealed record TrainingHistory
{
    public static readonly TrainingHistory Empty = new();

    /// <summary>Egzersiz -> tamamlanmis oturumlardaki maruziyetler (eskiden yeniye).</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<ExerciseExposure>> ExposuresByExercise { get; init; }
        = new Dictionary<long, IReadOnlyList<ExerciseExposure>>();
    public IReadOnlyList<FeedbackSignal> RecentFeedback { get; init; } = Array.Empty<FeedbackSignal>();

    /// <summary>Son 4 haftada planlanan antrenman gunu sayisi (aktif plana gore).</summary>
    public int ScheduledSessionsLast4Weeks { get; init; }

    /// <summary>Son 4 haftada tamamlanan plan oturumu sayisi.</summary>
    public int CompletedPlanSessionsLast4Weeks { get; init; }

    /// <summary>Mevcut / onceki programdaki egzersiz kimlikleri (sureklilik icin).</summary>
    public IReadOnlySet<long> CurrentProgramExerciseIds { get; init; } = new HashSet<long>();

    public double? CompletionRate => ScheduledSessionsLast4Weeks <= 0
        ? null
        : Math.Min(1d, (double)CompletedPlanSessionsLast4Weeks / ScheduledSessionsLast4Weeks);
}

/// <summary>Bir gunun hedefi (split sablonundan).</summary>
public sealed record DayTemplate(string Code, string Title, string Focus, IReadOnlyList<MuscleRegion> Regions);

/// <summary>Secilen split ve gerekcesi.</summary>
public sealed record SplitDecision(string SplitType, string Label, IReadOnlyList<DayTemplate> Days, IReadOnlyList<string> Reasons, int EffectiveDaysPerWeek);

/// <summary>Bir egzersiz icin set / tekrar / RIR / dinlenme recetesi.</summary>
public sealed record Prescription(int Sets, int RepsMin, int RepsMax, int TargetRir, int RestSeconds, int? DurationSeconds = null);

/// <summary>Programa yerlestirilmis egzersiz.</summary>
public sealed record PlannedExercise
{
    public int Order { get; init; }
    public CatalogExercise Exercise { get; init; } = null!;
    public MuscleRegion TargetRegion { get; init; }
    public Prescription Prescription { get; init; } = null!;
    public WeightGuidanceType WeightGuidance { get; init; }
    public decimal? SuggestedWeightKg { get; init; }
    public IReadOnlyList<long> AlternativeExerciseIds { get; init; } = Array.Empty<long>();
    public string SelectionReason { get; init; } = string.Empty;
}

public sealed record PlannedDay(DayOfWeek DayOfWeek, int Order, DayTemplate Template, IReadOnlyList<PlannedExercise> Exercises, int EstimatedMinutes);

/// <summary>Kas bolgesi basina haftalik hacim (yardimci kaslar kesirli sayilir).</summary>
public sealed record RegionVolume(MuscleRegion Region, decimal TargetSets, decimal PlannedDirectSets, decimal PlannedIndirectSets)
{
    public decimal PlannedTotalSets => PlannedDirectSets + PlannedIndirectSets;
}

/// <summary>Motorun urettigi program (henuz veritabanina yazilmamis).</summary>
public sealed record GeneratedProgram
{
    public string EngineVersion { get; init; } = TrainingEngine.Version;
    public TrainingFocus Focus { get; init; }
    public SplitDecision Split { get; init; } = null!;
    public IReadOnlyList<PlannedDay> Days { get; init; } = Array.Empty<PlannedDay>();
    public IReadOnlyList<RegionVolume> WeeklyVolume { get; init; } = Array.Empty<RegionVolume>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MetadataWarnings { get; init; } = Array.Empty<string>();
    public bool IsDeload { get; init; }
    public int BlockWeeks { get; init; } = 4;
}

/// <summary>Programi uretirken uygulanacak ayarlamalar (toparlanma / onceki versiyon).</summary>
public sealed record ProgramAdjustments
{
    public static readonly ProgramAdjustments None = new();

    /// <summary>Tum bolgelerin haftalik hacim carpani (or. 0.85 = %15 azalt).</summary>
    public decimal VolumeFactor { get; init; } = 1m;

    /// <summary>Bolge bazinda ek carpanlar.</summary>
    public IReadOnlyDictionary<MuscleRegion, decimal> RegionFactors { get; init; } = new Dictionary<MuscleRegion, decimal>();
    public bool IsDeload { get; init; }
    public int ExtraRestSeconds { get; init; }

    /// <summary>Kullanilmayacak egzersizler (ağrı / durağanlık nedeniyle degistirilecek).</summary>
    public IReadOnlySet<long> ExcludedExerciseIds { get; init; } = new HashSet<long>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

/// <summary>Motor surumu (planlara yazilir).</summary>
public static class TrainingEngine
{
    public const string Version = "2026.11-engine-v1";
}
