using System.Text.Json;
using System.Text.Json.Serialization;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.WorkoutLab;

/// <summary>
/// Antrenman Laboratuvari JSON sozlesmeleri. WorkoutPlanSimulation tablosundaki ProfileJson, ResultJson ve
/// QualityChecksJson kolonlari bu siniflarin camelCase JSON halidir. Admin API'si (MerfitApi) ProfileJson'u
/// ayni sozlesmeyle yazar ve diger kolonlari panele oldugu gibi iletir; alan adlari degistirilirken iki taraf
/// birlikte guncellenmelidir.
/// </summary>
public static class WorkoutLabJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>Admin'in sectigi onboarding tercihleri (ProfileJson).</summary>
public sealed class WorkoutPlanSimulationProfile
{
    // Temel bilgiler: antrenman algoritmasi bu alanlari kullanmaz (beslenme hesabinda kullanilir); test
    // kaydinin parcasi olarak saklanir.
    public int? Age { get; set; }
    public Gender? Gender { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? TargetWeightKg { get; set; }

    /// <summary>Uc ana hedeften biri. Bos ise algoritmanin varsayilani (Kas Gelistirme).</summary>
    public FitnessGoal? Goal { get; set; }

    /// <summary>Bos ise Beginner.</summary>
    public ExperienceLevel? ExperienceLevel { get; set; }

    /// <summary>
    /// Antrenman yeri. Algoritma lokasyonu dogrudan kullanmaz; mobil onboarding ile ayni sekilde Gym secilince
    /// katalogdaki tum ekipmanlar, digerlerinde secilen ekipmanlar kullaniciya atanmis sayilir.
    /// </summary>
    public TrainingLocation TrainingLocation { get; set; } = TrainingLocation.Home;

    /// <summary>Secilen Equipment.Id degerleri (Gym'de yok sayilir).</summary>
    public List<long> EquipmentIds { get; set; } = new();

    /// <summary>"Hic ekipmanim yok" secildi (EquipmentIds bos olmalidir).</summary>
    public bool NoEquipment { get; set; }

    /// <summary>Haftalik gun sayisi; bos ise algoritmanin varsayilani.</summary>
    public int? TrainingDaysPerWeek { get; set; }

    /// <summary>Belirli gunler ("Mon".."Sun"); doluysa gun sayisi bu listeden turetilir.</summary>
    public List<string> TrainingDays { get; set; } = new();

    /// <summary>Tercih edilen sure (dk); bos ise algoritmanin varsayilani.</summary>
    public int? WorkoutDurationMin { get; set; }

    /// <summary>"Free" veya "Plus".</summary>
    public bool IsPlus { get; set; }
}

public enum WorkoutPlanQualityCheckStatus
{
    Passed,
    Warning,
    Failed,
}

/// <summary>Tek bir otomatik kalite kontrolu.</summary>
public sealed class WorkoutPlanQualityCheck
{
    /// <summary>Kararli kod (orn. "DuplicateExercises"); karsilastirmada eslestirme icin kullanilir.</summary>
    public required string Code { get; init; }
    public required string Title { get; init; }
    public WorkoutPlanQualityCheckStatus Status { get; init; }

    /// <summary>Sonucun nedeni.</summary>
    public required string Message { get; init; }

    /// <summary>Kontrolun kurali (hangi esiklerle neye bakildigi).</summary>
    public required string Rule { get; init; }

    public List<string> Details { get; init; } = new();
}

/// <summary>Uretilen programin anlik goruntusu (ResultJson).</summary>
public sealed class WorkoutPlanSimulationResult
{
    public required string AlgorithmVersion { get; init; }
    public required string OptionsFingerprint { get; init; }
    public DateTime GeneratedAt { get; init; }

    public required string PlanName { get; init; }
    public required string Summary { get; init; }

    /// <summary>Algoritmanin kullandigi (normalize edilmis) hedef.</summary>
    public FitnessGoal Goal { get; init; }
    public ExperienceLevel Experience { get; init; }
    public DifficultyLevel TargetDifficulty { get; init; }
    public int TrainingDays { get; init; }
    public bool UsedPreferredDays { get; init; }
    public int DurationPreferenceMin { get; init; }
    public bool UsedDefaultDuration { get; init; }
    public int DurationToleranceMin { get; init; }
    public int MaxDurationMin { get; init; }
    public bool IncludePremium { get; init; }

    /// <summary>"GymAllEquipment", "Selected" veya "None".</summary>
    public required string EquipmentResolution { get; init; }

    /// <summary>Algoritmaya verilen ekipmanlar (Gym'de katalogdaki tumu).</summary>
    public List<SimulationEquipment> Equipment { get; init; } = new();

    /// <summary>Profilde secilen ama katalogda bulunamayan ekipman kimlikleri (yok sayildi).</summary>
    public List<long> UnknownEquipmentIds { get; init; } = new();

    public required string PoolStage { get; init; }
    public bool PremiumLastResortUsed { get; init; }
    public int EligibleCount { get; init; }
    public int MainPoolCount { get; init; }
    public bool CardioDominantMainDaysFallback { get; init; }
    public int CardioTarget { get; init; }
    public int CardioDays { get; init; }

    /// <summary>Set/tekrar/dinlenme kurallari (bu hedef ve deneyim icin).</summary>
    public int SetsRule { get; init; }
    public int RepsMinRule { get; init; }
    public int RepsMaxRule { get; init; }
    public int RestSecondsRule { get; init; }
    public int MaxExercisesPerDay { get; init; }

    public List<SimulationDay> Days { get; init; } = new();

    public required SimulationMetrics Metrics { get; init; }
}

public sealed class SimulationEquipment
{
    public long Id { get; init; }
    public required string Name { get; init; }
}

public sealed class SimulationDay
{
    /// <summary>"Mon".."Sun".</summary>
    public required string DayOfWeek { get; init; }

    /// <summary>Pazartesi = 0 ... Pazar = 6.</summary>
    public int DayIndex { get; init; }

    public required SimulationWorkout Workout { get; init; }
    public SimulationWorkout? Cardio { get; init; }

    /// <summary>
    /// Ana antrenmanin Kardiyo puani karma esiginin (WorkoutPlanGenerationOptions.MixedWorkoutCardioScoreThreshold)
    /// ustunde; algoritma bu gunu kardiyo bileseni sayar ve ayrica kardiyo eklemez.
    /// </summary>
    public bool IsMixedCardioDay { get; init; }

    /// <summary>Ana antrenman + antrenman sonu kardiyo suresi (katalogdaki Workout.DurationMin degerleri).</summary>
    public int EstimatedDurationMin { get; init; }

    /// <summary>Ana antrenmanin katalogda programa sigmayan (MaxExercisesPerDay) egzersiz sayisi.</summary>
    public int TruncatedExerciseCount { get; init; }

    /// <summary>Algoritmanin urettigi plan egzersizleri (set/tekrar/dinlenme algoritmadan).</summary>
    public List<SimulationExercise> Exercises { get; init; } = new();

    /// <summary>Antrenman sonu kardiyonun katalog egzersizleri (degerler katalogdan; algoritma bunlari degistirmez).</summary>
    public List<SimulationExercise> CardioExercises { get; init; } = new();
}

public sealed class SimulationWorkout
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public DifficultyLevel Difficulty { get; init; }
    public int DurationMin { get; init; }
    public long? MuscleGroupId { get; init; }
    public string? MuscleGroupName { get; init; }
    public bool IsPremium { get; init; }
    public bool IsFeatured { get; init; }
    public List<SimulationEquipment> RequiredEquipment { get; init; } = new();
    public decimal? BaseScore { get; init; }
    public decimal? FitScore { get; init; }
    public int? Hypertrophy { get; init; }
    public int? Strength { get; init; }
    public int? CardioScore { get; init; }
    public bool IsCardioDominant { get; init; }
}

public sealed class SimulationExercise
{
    public long ExerciseId { get; init; }
    public required string Name { get; init; }
    public int Order { get; init; }
    public long? MuscleGroupId { get; init; }
    public string? MuscleGroupName { get; init; }

    /// <summary>Egzersiz katalogundaki serbest metin ekipman bilgisi (Exercise.EquipmentType).</summary>
    public string? EquipmentType { get; init; }

    public int Sets { get; init; }
    public int? Reps { get; init; }
    public int? RepsMax { get; init; }
    public int? RestSeconds { get; init; }
    public int? DurationSeconds { get; init; }

    /// <summary>"Main" (ana antrenman, algoritma degerleri) veya "Cardio" (antrenman sonu kardiyo, katalog degerleri).</summary>
    public required string Kind { get; init; }
}

public sealed class SimulationMetrics
{
    public int DayCount { get; init; }
    public int TotalExercises { get; init; }
    public int UniqueExercises { get; init; }
    public int TotalSets { get; init; }
    public int CardioExerciseCount { get; init; }
    public decimal AverageDurationMin { get; init; }
    public decimal AverageExercisesPerDay { get; init; }
    public int UsedEquipmentCount { get; init; }
    public List<SimulationEquipment> UsedEquipment { get; init; } = new();
    public List<SimulationMuscleGroupShare> MuscleGroups { get; init; } = new();
    public List<SimulationRepsRangeShare> RepsRanges { get; init; } = new();
}

public sealed class SimulationMuscleGroupShare
{
    public long? MuscleGroupId { get; init; }
    public required string Name { get; init; }
    public int ExerciseCount { get; init; }
    public int Sets { get; init; }
    public int DayCount { get; init; }
}

public sealed class SimulationRepsRangeShare
{
    /// <summary>"8-12", "30 sn" gibi.</summary>
    public required string Label { get; init; }
    public int ExerciseCount { get; init; }
}
