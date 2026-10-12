using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "NutritionCalculation" bolumune karsilik gelen ayarlar.
/// NutritionCalculator servisinin kullandigi tum katsayi/sabitleri barindirir; boylece
/// hesaplama mantigi icinde magic number kullanilmaz ve degerler kod degistirilmeden
/// (konfigurasyon uzerinden) ayarlanabilir.
/// </summary>
public class NutritionCalculationOptions
{
    public const string SectionName = "NutritionCalculation";

    // --- Eksik veri varsayilanlari ---
    // Yalnizca esnek hesaplamada (INutritionCalculator.Calculate: kayit aninda ilk hedef, gunluk beslenme
    // ekranindaki hedef) kullanilir. Kisisel plan uretimi (Evaluate) bu varsayilanlari ASLA kullanmaz;
    // eksik veride acik bir hata dondurur.

    /// <summary>Profilde kilo (WeightKg) bilgisi eksikse esnek hesaplamada kullanilacak kilo (kg).</summary>
    public decimal DefaultWeightKg { get; set; } = 70m;

    /// <summary>Profilde boy (HeightCm) bilgisi eksikse esnek hesaplamada kullanilacak boy (cm).</summary>
    public decimal DefaultHeightCm { get; set; } = 170m;

    /// <summary>Profilde dogum tarihi eksikse esnek hesaplamada kullanilacak yas.</summary>
    public int DefaultAge { get; set; } = 30;

    /// <summary>Hesaplanan/verilen yasin altina inemeyecegi minimum deger (esnek hesaplama).</summary>
    public int MinimumAge { get; set; } = 15;

    // --- Gecerlilik araliklari (kisisel plan uretimi icin) ---

    /// <summary>Kisisel hedef hesaplanabilecek en dusuk yas. Mifflin-St Jeor yetiskinler icin gecerlidir.</summary>
    public int PlausibleMinAge { get; set; } = 15;

    /// <summary>Kisisel hedef hesaplanabilecek en yuksek yas.</summary>
    public int PlausibleMaxAge { get; set; } = 100;

    /// <summary>Gecerli kabul edilen en dusuk kilo (kg).</summary>
    public decimal PlausibleMinWeightKg { get; set; } = 30m;

    /// <summary>Gecerli kabul edilen en yuksek kilo (kg).</summary>
    public decimal PlausibleMaxWeightKg { get; set; } = 300m;

    /// <summary>Gecerli kabul edilen en dusuk boy (cm).</summary>
    public decimal PlausibleMinHeightCm { get; set; } = 120m;

    /// <summary>Gecerli kabul edilen en yuksek boy (cm).</summary>
    public decimal PlausibleMaxHeightCm { get; set; } = 230m;

    // Mifflin-St Jeor BMR formulu katsayilari:
    // Erkek: (10 * kg) + (6.25 * cm) - (5 * yas) + 5
    // Kadin: (10 * kg) + (6.25 * cm) - (5 * yas) - 161
    // Diger/belirtilmemis: (10 * kg) + (6.25 * cm) - (5 * yas) - 78 (erkek/kadin sabitlerinin ortalamasi)

    /// <summary>BMR formulunde kilo (kg) ile carpilan katsayi.</summary>
    public decimal BmrWeightCoefficient { get; set; } = 10m;

    /// <summary>BMR formulunde boy (cm) ile carpilan katsayi.</summary>
    public decimal BmrHeightCoefficient { get; set; } = 6.25m;

    /// <summary>BMR formulunde yas ile carpilan katsayi.</summary>
    public decimal BmrAgeCoefficient { get; set; } = 5m;

    /// <summary>BMR formulunde cinsiyete gore eklenen sabit deger (Male).</summary>
    public decimal BmrMaleConstant { get; set; } = 5m;

    /// <summary>BMR formulunde cinsiyete gore eklenen sabit deger (Female).</summary>
    public decimal BmrFemaleConstant { get; set; } = -161m;

    /// <summary>BMR formulunde cinsiyet belirtilmemis/diger oldugunda eklenen sabit deger.</summary>
    public decimal BmrOtherGenderConstant { get; set; } = -78m;

    /// <summary>
    /// Aktivite seviyesine gore BMR'yi TDEE'ye cevirmek icin kullanilan carpanlar
    /// (standart Mifflin-St Jeor / Harris-Benedict aktivite faktorleri).
    /// </summary>
    public Dictionary<ActivityLevel, decimal> ActivityMultipliers { get; set; } = new()
    {
        [ActivityLevel.Sedentary] = 1.2m,
        [ActivityLevel.LightlyActive] = 1.375m,
        [ActivityLevel.ModeratelyActive] = 1.55m,
        [ActivityLevel.VeryActive] = 1.725m,
        [ActivityLevel.ExtraActive] = 1.9m,
    };

    /// <summary>
    /// Haftalik antrenman gun sayisinin isaret ettigi en dusuk aktivite carpani. Aktivite seviyesi yoksa
    /// carpan buradan alinir; ikisi de varsa buyuk olani kullanilir (haftada 5 gun antrenman yapan biri
    /// "hareketsiz" sayilmaz).
    /// </summary>
    public Dictionary<int, decimal> TrainingDaysActivityMultipliers { get; set; } = new()
    {
        [0] = 1.2m,
        [1] = 1.2m,
        [2] = 1.375m,
        [3] = 1.375m,
        [4] = 1.55m,
        [5] = 1.55m,
        [6] = 1.725m,
        [7] = 1.725m,
    };

    /// <summary>Aktivite seviyesi ve antrenman gunu bilinmiyorsa esnek hesaplamada kullanilacak carpan.</summary>
    public decimal DefaultActivityMultiplier { get; set; } = 1.375m;

    /// <summary>
    /// Fitness hedefine gore TDEE'ye uygulanan oransal kalori duzeltmesi (-0.20 = %20 acik).
    /// Muhafazakar tutulur; mutlak buyuklugu GoalCalorieAdjustmentCaps ile sinirlanir.
    /// </summary>
    public Dictionary<FitnessGoal, decimal> GoalCalorieAdjustmentPercentages { get; set; } = new()
    {
        [FitnessGoal.LoseWeight] = -0.20m,
        [FitnessGoal.BuildMuscle] = 0.10m,
        [FitnessGoal.GetStronger] = 0.05m,
        [FitnessGoal.ImproveFitness] = 0m,
        [FitnessGoal.MaintainWeight] = 0m,
        [FitnessGoal.ImproveEndurance] = 0m,
    };

    /// <summary>Hedefe gore kalori duzeltmesinin mutlak ust siniri (kcal/gun).</summary>
    public Dictionary<FitnessGoal, decimal> GoalCalorieAdjustmentCaps { get; set; } = new()
    {
        [FitnessGoal.LoseWeight] = 500m,
        [FitnessGoal.BuildMuscle] = 300m,
        [FitnessGoal.GetStronger] = 200m,
    };

    /// <summary>Gunluk kalori hedefinin inebilecegi minimum guvenli deger. Hedef ayrica BMR'nin altina inmez.</summary>
    public decimal MinimumDailyCalories { get; set; } = 1200m;

    /// <summary>Hedefe gore referans kilo basina gunluk protein (g/kg).</summary>
    public Dictionary<FitnessGoal, decimal> ProteinGramsPerKg { get; set; } = new()
    {
        [FitnessGoal.LoseWeight] = 1.8m,
        [FitnessGoal.BuildMuscle] = 1.8m,
        [FitnessGoal.GetStronger] = 1.7m,
        [FitnessGoal.ImproveEndurance] = 1.4m,
        [FitnessGoal.ImproveFitness] = 1.4m,
        [FitnessGoal.MaintainWeight] = 1.2m,
    };

    /// <summary>Hedef bilinmiyorsa (yalnizca esnek hesaplama) kullanilan protein (g/kg).</summary>
    public decimal DefaultProteinGramsPerKg { get; set; } = 1.4m;

    /// <summary>
    /// Protein hesabinda kullanilan referans kilonun karsilik geldigi en yuksek VKI. Yuksek VKI'de protein,
    /// toplam kilo yerine bu VKI'deki kiloya gore hesaplanir (asiri yuksek protein hedefini onler).
    /// </summary>
    public decimal ProteinReferenceMaxBmi { get; set; } = 27m;

    /// <summary>Proteinin gunluk kaloriden alabilecegi en yuksek pay.</summary>
    public decimal MaxProteinCaloriePercentage { get; set; } = 0.35m;

    /// <summary>Hedefe gore yagin gunluk kaloriden aldigi pay.</summary>
    public Dictionary<FitnessGoal, decimal> FatCaloriePercentages { get; set; } = new()
    {
        [FitnessGoal.LoseWeight] = 0.30m,
        [FitnessGoal.BuildMuscle] = 0.25m,
        [FitnessGoal.GetStronger] = 0.27m,
        [FitnessGoal.ImproveEndurance] = 0.25m,
        [FitnessGoal.ImproveFitness] = 0.28m,
        [FitnessGoal.MaintainWeight] = 0.30m,
    };

    /// <summary>Hedef bilinmiyorsa (yalnizca esnek hesaplama) kullanilan yag payi.</summary>
    public decimal DefaultFatCaloriePercentage { get; set; } = 0.30m;

    /// <summary>Karbonhidrat cok dusuk kalirsa yagin dusurulebilecegi en alt pay.</summary>
    public decimal MinimumFatCaloriePercentage { get; set; } = 0.20m;

    /// <summary>Hedeflenen en dusuk gunluk karbonhidrat (gram); altina dusmemek icin yag azaltilir.</summary>
    public decimal MinimumCarbsGrams { get; set; } = 100m;

    /// <summary>1 gram proteinin kalori karsiligi.</summary>
    public decimal ProteinCaloriesPerGram { get; set; } = 4m;

    /// <summary>1 gram karbonhidratin kalori karsiligi.</summary>
    public decimal CarbsCaloriesPerGram { get; set; } = 4m;

    /// <summary>1 gram yagin kalori karsiligi.</summary>
    public decimal FatCaloriesPerGram { get; set; } = 9m;

    /// <summary>Kilogram basina onerilen gunluk su miktari (ml).</summary>
    public decimal WaterMlPerKg { get; set; } = 35m;
}
