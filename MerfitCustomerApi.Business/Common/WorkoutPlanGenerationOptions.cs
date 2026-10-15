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
    /// Uc ana hedef icin Hipertrofi/Kuvvet/Kardiyo agirliklari (yuzde, toplam 100). Kullanicinin hedefi
    /// (aktif UserGoal -> UserProfile.Goal -> Kas Gelistirme) once <see cref="FitnessGoals.Normalize(FitnessGoal)"/>
    /// ile uc hedeften birine donusturulur, sonra bu tablodan bir agirlik profili secilir.
    /// Degerler urunun ilk surumu icin baslangic degerleridir; bilimsel olarak dogrulanmis degildir.
    /// </summary>
    public Dictionary<FitnessGoal, GoalAttributeWeights> PrimaryAttributeWeightsByGoal { get; set; } = new()
    {
        [FitnessGoal.BuildMuscle] = new() { Hypertrophy = 65, Strength = 25, Cardio = 10 },
        [FitnessGoal.GetStronger] = new() { Hypertrophy = 25, Strength = 65, Cardio = 10 },
        [FitnessGoal.LoseWeight] = new() { Hypertrophy = 45, Strength = 25, Cardio = 30 },
    };

    // --- Kardiyo ve toparlanma ---

    /// <summary>
    /// Kardiyo puani hem Hipertrofi hem Kuvvet puanindan yuksek olan antrenmanlar "kardiyo agirlikli"
    /// sayilir ve ana (direnc) antrenman gunlerine atanmaz; yalnizca antrenman sonu kardiyo olarak
    /// kullanilir. Hedef ne olursa olsun program yalnizca kardiyodan olusmaz. Katalogda hic direnc
    /// antrenmani bulunamazsa (veri sorunu) program uretilememesi yerine tum uygun antrenmanlar kullanilir.
    /// </summary>
    public bool ExcludeCardioDominantFromMainDays { get; set; } = true;

    /// <summary>
    /// Hedefe gore haftada kac antrenman gunune antrenman sonu kardiyo (WorkoutPlanDay.CardioWorkoutId)
    /// eklenecegi. Kardiyo bir hedef degil, programin bir bilesenidir. Gun sayisindan fazlaysa gun
    /// sayisiyla sinirlanir.
    /// </summary>
    public Dictionary<FitnessGoal, int> CardioSessionsPerWeekByGoal { get; set; } = new()
    {
        [FitnessGoal.BuildMuscle] = 1,
        [FitnessGoal.GetStronger] = 1,
        [FitnessGoal.LoseWeight] = 3,
    };

    /// <summary>
    /// Ana antrenmanin Kardiyo puani bu degere esit veya ustundeyse (karma antrenman) o gun zaten bir
    /// kardiyo bileseni sayilir ve ayrica antrenman sonu kardiyo eklenmez.
    /// </summary>
    public int MixedWorkoutCardioScoreThreshold { get; set; } = 60;

    /// <summary>Antrenman sonu kardiyo icin en kisa sure (dakika); kalan sure bundan azsa eklenmez.</summary>
    public int CardioFinisherMinMinutes { get; set; } = 10;

    /// <summary>Antrenman sonu kardiyo icin en uzun sure (dakika).</summary>
    public int CardioFinisherMaxMinutes { get; set; } = 30;

    /// <summary>
    /// Ayni ana kas grubu takvimde ardisik iki gunde (orn. Pazartesi-Sali) calisiyorsa
    /// <see cref="ConsecutiveMuscleGroupPenaltyScore"/>'a ek olarak dusulen puan (toparlanma).
    /// </summary>
    public int AdjacentDayMuscleGroupPenaltyScore { get; set; } = 20;

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
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan set sayisi.</summary>
    public int DefaultSets { get; set; } = 3;

    /// <summary>
    /// Hedefe gore hedef tekrar araligi (Min/Max). Plan egzersizi Reps = Min, RepsMax = Max olarak
    /// kaydedilir; cift progresyon (double progression) bu araligi kullanir. Yag kaybinda da kas
    /// korunmasi icin direnc antrenmani orta tekrar araliginda kalir.
    /// Eslesme bulunamazsa <see cref="DefaultRepsRange"/> kullanilir.
    /// </summary>
    public Dictionary<string, (int Min, int Max)> RepsRangeByGoal { get; set; } = new()
    {
        ["BuildMuscle"] = (8, 12),
        ["GetStronger"] = (3, 6),
        ["LoseWeight"] = (10, 15),
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan tekrar araligi.</summary>
    public (int Min, int Max) DefaultRepsRange { get; set; } = (10, 12);

    /// <summary>Hedefe gore setler arasi dinlenme suresi (saniye).</summary>
    public Dictionary<FitnessGoal, int> RestSecondsByGoal { get; set; } = new()
    {
        [FitnessGoal.GetStronger] = 120,
        [FitnessGoal.BuildMuscle] = 90,
        [FitnessGoal.LoseWeight] = 60,
    };

    /// <summary>Eslesme bulunamadiginda kullanilacak varsayilan dinlenme suresi (saniye).</summary>
    public int DefaultRestSeconds { get; set; } = 60;
}
