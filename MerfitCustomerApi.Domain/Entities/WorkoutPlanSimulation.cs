using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Antrenman Laboratuvari test kaydi. Admin'in sectigi onboarding tercihleri (ProfileJson), gercek
/// WorkoutPlanGenerator'in bu tercihlerle urettigi programin anlik goruntusu (ResultJson), otomatik kalite
/// kontrolleri ve admin degerlendirmesi burada saklanir.
///
/// Gercek kullanicilardan tamamen bagimsizdir: UserProfile/WorkoutPlan/PersonalizationJob gibi kullanici
/// tablolarina iliski veya yazma yoktur. CreatedUser, testi baslatan admin'in kimligidir.
/// Tablo bu API'nin migration'i ile olusturulur; admin API'si (MerfitApi) ayni tabloyu okur ve yazar.
/// </summary>
public class WorkoutPlanSimulation : BaseEntity
{
    /// <summary>Admin'in teste verdigi istege bagli ad (orn. "Ev - ekipmansiz - 3 gun").</summary>
    public string? Name { get; set; }

    public WorkoutPlanSimulationStatus Status { get; set; } = WorkoutPlanSimulationStatus.Pending;

    /// <summary>Simulasyon tercihleri (JSON, bkz. WorkoutPlanSimulationProfile).</summary>
    public string ProfileJson { get; set; } = "{}";

    // --- Listeleme/filtreleme icin profilden kopyalanan alanlar (JSON'u acmadan sorgulanir) ---

    /// <summary>Uc ana hedeften biri (BuildMuscle, GetStronger, LoseWeight).</summary>
    public FitnessGoal Goal { get; set; }

    public ExperienceLevel ExperienceLevel { get; set; }

    /// <summary>Haftalik antrenman gunu sayisi (belirli gunler secildiyse onlarin sayisi).</summary>
    public int TrainingDaysPerWeek { get; set; }

    /// <summary>Tercih edilen antrenman suresi; bos ise algoritmanin varsayilani kullanilir.</summary>
    public int? WorkoutDurationMin { get; set; }

    public TrainingLocation TrainingLocation { get; set; }

    /// <summary>Aktif Plus aboneligi simule edildi mi.</summary>
    public bool IsPlus { get; set; }

    /// <summary>Tekrar calistirilan testin kimligi (varsa).</summary>
    public long? SourceSimulationId { get; set; }

    // --- Islem sonucu ---

    /// <summary>Programi ureten algoritma surumu (WorkoutPlanGenerator.AlgorithmVersion).</summary>
    public string? AlgorithmVersion { get; set; }

    /// <summary>Uretim anindaki WorkoutPlanGenerationOptions degerlerinin kisa ozeti (agirlik degisikliklerini ayirt eder).</summary>
    public string? OptionsFingerprint { get; set; }

    /// <summary>Uretilen programin anlik goruntusu (JSON, bkz. WorkoutPlanSimulationResult).</summary>
    public string? ResultJson { get; set; }

    /// <summary>Otomatik kalite kontrol sonuclari (JSON dizi).</summary>
    public string? QualityChecksJson { get; set; }

    public int PassedCheckCount { get; set; }

    public int WarningCheckCount { get; set; }

    public int FailedCheckCount { get; set; }

    /// <summary>Program uretilemediyse algoritmanin hata mesaji.</summary>
    public string? ErrorMessage { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    // --- Admin degerlendirmesi ---

    public WorkoutPlanEvaluationRating? Rating { get; set; }

    /// <summary>Eksik bulunan noktalar.</summary>
    public string? EvaluationMissingPoints { get; set; }

    /// <summary>Sorunlu gun/egzersiz isaretleri (JSON dizi: gun, egzersiz, not).</summary>
    public string? EvaluationProblemItemsJson { get; set; }

    /// <summary>Yeni egzersiz onerileri.</summary>
    public string? EvaluationExerciseSuggestions { get; set; }

    /// <summary>Genel aciklama.</summary>
    public string? EvaluationComment { get; set; }

    public DateTime? EvaluatedAt { get; set; }

    public long? EvaluatedBy { get; set; }
}
