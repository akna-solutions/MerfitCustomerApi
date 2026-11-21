namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Kisisellestirilmis antrenman motorunun ayarlanabilir kurallari (appsettings: "TrainingEngine").
/// Varsayilanlar ilk surum icin konulmus baslangic degerleridir; bilimsel kaynaklarin genel onerilerine
/// (haftalik kas basina ~10-20 set, cogu sette 1-3 RIR) dayanir ama kullaniciya ozel dogrulanmis degildir.
/// </summary>
public class TrainingEngineOptions
{
    public const string SectionName = "TrainingEngine";

    /// <summary>Buyuk kas gruplari icin deneyime gore haftalik temel set (yardimci kaslar 0.5 sayilir).</summary>
    public decimal BeginnerWeeklySets { get; set; } = 8m;
    public decimal IntermediateWeeklySets { get; set; } = 12m;
    public decimal AdvancedWeeklySets { get; set; } = 15m;

    /// <summary>Kucuk kas gruplari (biceps, triceps, baldir, core) temel hacim carpani.</summary>
    public decimal SmallMuscleFactor { get; set; } = 0.67m;

    /// <summary>Kalca dogrudan hacim carpani (squat / hinge zaten calistirdigi icin).</summary>
    public decimal GluteDirectFactor { get; set; } = 0.5m;

    /// <summary>Oncelikli kas gruplarina uygulanan artis.</summary>
    public decimal PriorityMuscleFactor { get; set; } = 1.3m;

    /// <summary>Yardimci kasin haftalik hacimdeki agirligi (bir set = 0.5 set).</summary>
    public decimal SecondaryMuscleSetWeight { get; set; } = 0.5m;

    /// <summary>Bolge basina haftalik ust sinir (asiri hacmi onler).</summary>
    public decimal MaxWeeklySetsLargeMuscle { get; set; } = 20m;
    public decimal MaxWeeklySetsSmallMuscle { get; set; } = 14m;

    /// <summary>Tek bir egzersizde set sinirlari.</summary>
    public int MinSetsPerExercise { get; set; } = 2;
    public int MaxSetsPerExercise { get; set; } = 5;

    /// <summary>Bir gunde bir bolge icin en fazla egzersiz.</summary>
    public int MaxExercisesPerRegionPerDay { get; set; } = 2;

    /// <summary>Bir gunde en fazla egzersiz.</summary>
    public int MaxExercisesPerDay { get; set; } = 8;

    /// <summary>Set basina tahmini calisma suresi (sn) ve antrenman basi isinma (dk).</summary>
    public int WorkSecondsPerSet { get; set; } = 40;
    public int WarmupMinutes { get; set; } = 5;

    /// <summary>Baslangic seviyesinde haftalik antrenman gunu ust siniri; herkes icin ust sinir.</summary>
    public int BeginnerMaxDaysPerWeek { get; set; } = 4;
    public int MaxDaysPerWeek { get; set; } = 6;

    /// <summary>Devamlilik bu oranin altindaysa (en az 4 planli gun) hacim azaltilir.</summary>
    public double LowAdherenceThreshold { get; set; } = 0.6;
    public decimal LowAdherenceVolumeFactor { get; set; } = 0.8m;

    /// <summary>Yorgunluk / toparlanma sinyali varsa hacim carpani.</summary>
    public decimal HighFatigueVolumeFactor { get; set; } = 0.85m;

    /// <summary>Deload versiyonunda set carpani ve yuk azaltma yuzdesi.</summary>
    public decimal DeloadSetFactor { get; set; } = 0.6m;
    public decimal DeloadLoadReductionPercent { get; set; } = 10m;

    /// <summary>Program versiyonunun blok uzunlugu (hafta).</summary>
    public int BlockWeeks { get; set; } = 4;

    /// <summary>Iki ardisik maruziyette tahmini 1RM bu yuzdeden fazla duserse "dusus" sayilir.</summary>
    public decimal PerformanceDropPercent { get; set; } = 5m;

    /// <summary>Kalici dususte yuk azaltma yuzdesi.</summary>
    public decimal DeclineLoadReductionPercent { get; set; } = 7.5m;

    /// <summary>Ilerleme olmadan gecen bu kadar maruziyetten sonra egzersiz rotasyonu onerilir.</summary>
    public int StallExposuresForRotation { get; set; } = 4;

    /// <summary>Rahatsizlik bildirimi bu kadar gun gecerli sayilir.</summary>
    public int DiscomfortLookbackDays { get; set; } = 21;

    /// <summary>
    /// Kas grubu adi / slug'i -> kanonik bolge (Chest, Back, Shoulders, Biceps, Triceps, Quadriceps, Hamstrings,
    /// Glutes, Calves, Core, Forearms, FullBody, Cardio). Anahtar kelime eslemesini ezmek icin kullanilir.
    /// </summary>
    public Dictionary<string, string> MuscleGroupAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
