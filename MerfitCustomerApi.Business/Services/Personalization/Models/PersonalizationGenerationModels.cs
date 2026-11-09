using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Personalization.Models;

/// <summary>
/// WorkoutPlanGenerator'in ciktisi. Henuz veritabanina yazilmamis (Id = 0) entity graph'i tasir;
/// kaydetme ve transaction sorumlulugu PersonalizationJobProcessor'a aittir.
/// </summary>
public class WorkoutPlanGenerationResult
{
    /// <summary>Ana WorkoutPlan kaydi (henuz UserId disindaki Id'ler atanmamis).</summary>
    public required WorkoutPlan Plan { get; init; }

    /// <summary>Planin gunleri ve her gune ait kisisellestirilmis egzersizler.</summary>
    public required List<WorkoutPlanDayResult> Days { get; init; }

    /// <summary>
    /// Kullaniciya gosterilebilecek, tamamen kurallardan uretilmis (AI kullanilmadan) kisa
    /// aciklama. Orn: "Başlangıç seviyene, haftada 3 gün antrenman tercihine ve mevcut
    /// ekipmanlarına göre hazırlandı."
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Algoritmanin bu uretimde aldigi kararlar (hangi aday havuzu/fallback kullanildi, aday puanlari).
    /// Yalnizca okuma/inceleme amaclidir (Antrenman Laboratuvari, testler); kaydedilmez ve plani etkilemez.
    /// </summary>
    public WorkoutPlanGenerationDiagnostics? Diagnostics { get; init; }
}

/// <summary>
/// WorkoutPlanGenerator'in girdisi. Kullanici akisinda veritabanindaki profilden
/// (UserProfile, aktif UserGoal, UserEquipment, Subscription), Antrenman Laboratuvari'nda ise admin'in
/// sectigi tercihlerden olusturulur. Iki yol da ayni algoritmayi calistirir.
/// </summary>
public sealed class WorkoutPlanGenerationInput
{
    /// <summary>Plan sahibi kullanici; simulasyonda 0 (plan kaydedilmez).</summary>
    public long UserId { get; init; }

    /// <summary>Ham hedef (eski hedefler dahil); algoritma FitnessGoals.Normalize ile uc ana hedefe cevirir.</summary>
    public FitnessGoal? Goal { get; init; }

    /// <summary>Bos ise Beginner.</summary>
    public ExperienceLevel? ExperienceLevel { get; init; }

    /// <summary>Bos ise varsayilan (WorkoutPlanGenerationOptions.DefaultTrainingDaysPerWeek); 2-7 araligina sinirlanir.</summary>
    public int? TrainingDaysPerWeek { get; init; }

    /// <summary>Kullanicinin tek tek sectigi gunler. Doluysa gun sayisi bu listeden turetilir.</summary>
    public IReadOnlyList<DayOfWeek> PreferredTrainingDays { get; init; } = Array.Empty<DayOfWeek>();

    /// <summary>Bos ise varsayilan (WorkoutPlanGenerationOptions.DefaultWorkoutDurationMin).</summary>
    public int? WorkoutDurationMin { get; init; }

    /// <summary>Kullanicinin sahip oldugu ekipmanlarin Equipment.Id degerleri; bos = ekipman yok.</summary>
    public IReadOnlyCollection<long> OwnedEquipmentIds { get; init; } = Array.Empty<long>();

    /// <summary>Aktif Plus aboneligi (Status = Active ve suresi dolmamis) var mi; Premium antrenman erisimini belirler.</summary>
    public bool HasActivePlusSubscription { get; init; }
}

/// <summary>Aday havuzunun hangi asamada olustugu (fallback zinciri).</summary>
public enum WorkoutCandidatePoolStage
{
    /// <summary>Kullanicinin seviyesi + sure toleransi + ekipman.</summary>
    Primary,

    /// <summary>Fallback-1: bitisik zorluk seviyeleri (sure ve ekipman kurallari ayni).</summary>
    AdjacentDifficulty,

    /// <summary>Fallback-2: ekipman gerektirmeyen tum aktif antrenmanlar (zorluk ve sure filtresi yok).</summary>
    EquipmentFreeAnyDifficulty,
}

/// <summary>Bir uretimde algoritmanin aldigi kararlar.</summary>
public sealed class WorkoutPlanGenerationDiagnostics
{
    public required string AlgorithmVersion { get; init; }
    public FitnessGoal Goal { get; init; }
    public ExperienceLevel Experience { get; init; }
    public DifficultyLevel TargetDifficulty { get; init; }
    public int TrainingDays { get; init; }
    public bool UsedPreferredDays { get; init; }
    public required IReadOnlyList<DayOfWeek> DayPattern { get; init; }
    public int DurationPreferenceMin { get; init; }
    public int MaxDurationMin { get; init; }
    public required IReadOnlyList<long> OwnedEquipmentIds { get; init; }
    public bool IncludePremium { get; init; }
    public WorkoutCandidatePoolStage PoolStage { get; init; }

    /// <summary>Fallback-3: ucretsiz havuz bos kaldigi icin Premium antrenmanlar son care olarak acildi.</summary>
    public bool PremiumLastResortUsed { get; init; }

    public int EligibleCount { get; init; }
    public int MainPoolCount { get; init; }

    /// <summary>Hic direnc antrenmani kalmadigi icin kardiyo agirlikli antrenmanlar ana gunlerde kullanildi.</summary>
    public bool CardioDominantMainDaysFallback { get; init; }

    /// <summary>Hedefe gore haftalik kardiyo hedefi (gun sayisiyla sinirli).</summary>
    public int CardioTarget { get; init; }

    /// <summary>Kardiyo bileseni olan gun sayisi (karma ana antrenman + antrenman sonu kardiyo).</summary>
    public int CardioDays { get; init; }

    public required IReadOnlyList<WorkoutCandidateInsight> Candidates { get; init; }
}

/// <summary>Uygun bir adayin puanlari.</summary>
public sealed class WorkoutCandidateInsight
{
    public long WorkoutId { get; init; }

    /// <summary>Ana (direnc) gunlerine aday mi; degilse yalnizca antrenman sonu kardiyo olabilir.</summary>
    public bool InMainPool { get; init; }

    /// <summary>Gun atamasindan bagimsiz temel skor (yalnizca ana havuzdaki adaylar icin).</summary>
    public decimal? BaseScore { get; init; }

    public decimal? FitScore { get; init; }
    public int? Hypertrophy { get; init; }
    public int? Strength { get; init; }
    public int? Cardio { get; init; }
    public bool IsCardioDominant { get; init; }
}

/// <summary>Tek bir WorkoutPlanDay ve o gune atanan kisisellestirilmis egzersizler.</summary>
public class WorkoutPlanDayResult
{
    public required WorkoutPlanDay Day { get; init; }
    public required List<WorkoutPlanExercise> Exercises { get; init; }
}

/// <summary>
/// NutritionPlanGenerator'in ciktisi. Henuz veritabanina yazilmamis (Id = 0) entity graph'i tasir.
/// </summary>
public class NutritionPlanGenerationResult
{
    public required NutritionPlan Plan { get; init; }
    public required List<NutritionPlanDayResult> Days { get; init; }
    public required string Summary { get; init; }

    /// <summary>
    /// Planin dayandigi, profilden yeniden hesaplanmis gunluk hedefler. Null degilse
    /// PersonalizationJobProcessor bunlari plan ile ayni transaction icinde NutritionGoal'a yazar
    /// (NutritionGoal profilden turetilen bir degerdir; bkz. NutritionPlanGenerator).
    /// </summary>
    public NutritionCalculationResult? Targets { get; init; }

    /// <summary>Gunluk toplamlarin tolerans disinda kaldigi gunler gibi, plana dair uyarilar.</summary>
    public List<string> Warnings { get; init; } = new();
}

/// <summary>Tek bir NutritionPlanDay ve o gune atanan ogunler.</summary>
public class NutritionPlanDayResult
{
    public required NutritionPlanDay Day { get; init; }
    public required List<NutritionPlanMealResult> Meals { get; init; }
}

/// <summary>Tek bir NutritionPlanMeal ve icindeki besin ogeleri.</summary>
public class NutritionPlanMealResult
{
    public required NutritionPlanMeal Meal { get; init; }
    public required List<NutritionPlanMealItem> Items { get; init; }
}
