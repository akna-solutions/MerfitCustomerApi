using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "WorkoutPlanGeneration" bolumune karsilik gelen ayarlar.
/// WorkoutPlanGenerator'in kullandigi tum skorlama agirliklari, set/tekrar kurallari ve
/// varsayilanlar buradan konfigure edilir; kod icinde magic number kullanilmaz.
/// </summary>
public class WorkoutPlanGenerationOptions
{
    public const string SectionName = "WorkoutPlanGeneration";

    /// <summary>TrainingDaysPerWeek belirtilmemisse kullanilacak varsayilan gun sayisi.</summary>
    public int DefaultTrainingDaysPerWeek { get; set; } = 3;

    /// <summary>Bir plan gunune atanabilecek maksimum egzersiz sayisi.</summary>
    public int MaxExercisesPerDay { get; set; } = 6;

    /// <summary>WorkoutDurationMin belirtilmemisse kullanilacak varsayilan sure (dakika).</summary>
    public int DefaultWorkoutDurationMin { get; set; } = 45;

    /// <summary>Bir antrenmanin, kullanicinin tercih ettigi sureyi asabilecegi tolerans (dakika).</summary>
    public int DurationToleranceMin { get; set; } = 15;

    // --- Skorlama agirliklari (toplam adaylari siralamak icin) ---

    /// <summary>Zorluk seviyesi tam eslestiginde eklenen puan.</summary>
    public int ExperienceMatchScore { get; set; } = 30;

    /// <summary>
    /// Zorluk seviyesi kullanicinin seviyesine bitisik oldugunda (fallback havuzlarindan gelen
    /// adaylar) eklenen kismi puan. Daha uzak seviyeler deneyim puani almaz.
    /// </summary>
    public int AdjacentExperienceMatchScore { get; set; } = 15;

    /// <summary>Sure kullanicinin tercihine yakinsa (tolerans icinde) eklenen puan.</summary>
    public int DurationMatchScore { get; set; } = 20;

    /// <summary>Workout "featured" (one cikan) ise eklenen puan.</summary>
    public int FeaturedBonusScore { get; set; } = 10;

    /// <summary>Son N gunde/planda zaten kullanilmis bir workout ise dusulen puan (tekrar onleme).</summary>
    public int RepeatPenaltyScore { get; set; } = 50;

    /// <summary>Ayni ana kas grubu bir onceki plan gununde de kullanildiysa dusulen puan (kas dengesi).</summary>
    public int ConsecutiveMuscleGroupPenaltyScore { get; set; } = 15;

    // --- Hedef uyumu (antrenman ozellik puanlari x hedef agirliklari) ---

    /// <summary>
    /// Hedef uyum puaninin (FitScore, 0-100) toplam skora en fazla ne kadar katki yapacagi.
    /// Katki = FitScore * GoalFitMaxScore / 100. Hic ozellik puani olmayan antrenmanlar bu katkiyi
    /// almaz ve yalnizca mevcut kriterlerle (deneyim, sure, featured) siralanir.
    /// </summary>
    public int GoalFitMaxScore { get; set; } = 40;

    /// <summary>
    /// FitnessGoal basina antrenman ozellik agirliklari (yuzde, toplam 100). Kullanicinin hedefi
    /// (aktif UserGoal -> UserProfile.Goal -> MaintainWeight) bu tablodan bir agirlik profili secer.
    /// Degerler urunun ilk surumu icin baslangic degerleridir; bilimsel olarak dogrulanmis degildir.
    /// </summary>
    public Dictionary<FitnessGoal, GoalAttributeWeights> AttributeWeightsByGoal { get; set; } = new()
    {
        [FitnessGoal.BuildMuscle] = new() { Hypertrophy = 50, Strength = 20, Cardio = 5, Endurance = 5, Mobility = 10, CoreStability = 10 },
        [FitnessGoal.GetStronger] = new() { Hypertrophy = 15, Strength = 50, Cardio = 5, Endurance = 5, Mobility = 10, CoreStability = 15 },
        [FitnessGoal.LoseWeight] = new() { Hypertrophy = 10, Strength = 10, Cardio = 35, Endurance = 25, Mobility = 10, CoreStability = 10 },
        [FitnessGoal.ImproveFitness] = new() { Hypertrophy = 15, Strength = 15, Cardio = 25, Endurance = 20, Mobility = 10, CoreStability = 15 },
        [FitnessGoal.MaintainWeight] = new() { Hypertrophy = 15, Strength = 15, Cardio = 25, Endurance = 20, Mobility = 10, CoreStability = 15 },
        [FitnessGoal.ImproveEndurance] = new() { Hypertrophy = 10, Strength = 10, Cardio = 25, Endurance = 35, Mobility = 10, CoreStability = 10 },
    };

    // --- Set/tekrar kurallari (hedef x deneyim seviyesine gore) ---

    /// <summary>
    /// (FitnessGoal, ExperienceLevel) kombinasyonuna gore set sayisi. Eslesme bulunamazsa
    /// <see cref="DefaultSets"/> kullanilir.
    /// </summary>
    public Dictionary<string, int> SetsByGoalAndExperience { get; set; } = new()
    {
        ["BuildMuscle:Beginner"] = 3,
        ["BuildMuscle:Intermediate"] = 4,
        ["BuildMuscle:Advanced"] = 5,
        ["GetStronger:Beginner"] = 3,
        ["GetStronger:Intermediate"] = 4,
        ["GetStronger:Advanced"] = 5,
        ["LoseWeight:Beginner"] = 3,
        ["LoseWeight:Intermediate"] = 3,
        ["LoseWeight:Advanced"] = 4,
        ["ImproveEndurance:Beginner"] = 2,
        ["ImproveEndurance:Intermediate"] = 3,
        ["ImproveEndurance:Advanced"] = 3,
        ["ImproveFitness:Beginner"] = 3,
        ["ImproveFitness:Intermediate"] = 3,
        ["ImproveFitness:Advanced"] = 4,
        ["MaintainWeight:Beginner"] = 3,
        ["MaintainWeight:Intermediate"] = 3,
        ["MaintainWeight:Advanced"] = 4,
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan set sayisi.</summary>
    public int DefaultSets { get; set; } = 3;

    /// <summary>
    /// (FitnessGoal, ExperienceLevel) kombinasyonuna gore hedef tekrar araligi (Min/Max).
    /// Eslesme bulunamazsa <see cref="DefaultRepsRange"/> kullanilir.
    /// </summary>
    public Dictionary<string, (int Min, int Max)> RepsRangeByGoal { get; set; } = new()
    {
        ["BuildMuscle"] = (8, 12),
        ["GetStronger"] = (3, 6),
        ["LoseWeight"] = (12, 15),
        ["ImproveEndurance"] = (15, 20),
        ["ImproveFitness"] = (10, 15),
        ["MaintainWeight"] = (10, 12),
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan tekrar araligi.</summary>
    public (int Min, int Max) DefaultRepsRange { get; set; } = (10, 12);

    /// <summary>Hedefe gore setler arasi dinlenme suresi (saniye).</summary>
    public Dictionary<FitnessGoal, int> RestSecondsByGoal { get; set; } = new()
    {
        [FitnessGoal.GetStronger] = 120,
        [FitnessGoal.BuildMuscle] = 90,
        [FitnessGoal.LoseWeight] = 45,
        [FitnessGoal.ImproveEndurance] = 30,
        [FitnessGoal.ImproveFitness] = 60,
        [FitnessGoal.MaintainWeight] = 60,
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan dinlenme suresi (saniye).</summary>
    public int DefaultRestSeconds { get; set; } = 60;
}
