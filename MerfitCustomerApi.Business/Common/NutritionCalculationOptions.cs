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

    /// <summary>Profilde kilo (WeightKg) bilgisi eksikse kullanilacak varsayilan kilo (kg).</summary>
    public decimal DefaultWeightKg { get; set; } = 70m;

    /// <summary>Profilde boy (HeightCm) bilgisi eksikse kullanilacak varsayilan boy (cm).</summary>
    public decimal DefaultHeightCm { get; set; } = 170m;

    /// <summary>Profilde dogum tarihi eksikse kullanilacak varsayilan yas.</summary>
    public int DefaultAge { get; set; } = 30;

    /// <summary>Hesaplanan/verilen yasin altina inemeyecegi minimum deger.</summary>
    public int MinimumAge { get; set; } = 15;

    // Mifflin-St Jeor BMR formulu katsayilari:
    // Erkek: (10 * kg) + (6.25 * cm) - (5 * yas) + 5
    // Kadin: (10 * kg) + (6.25 * cm) - (5 * yas) - 161
    // Diger: (10 * kg) + (6.25 * cm) - (5 * yas) - 78 (erkek/kadin sabitlerinin ortalamasi)

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

    /// <summary>Aktivite seviyesine gore BMR'yi TDEE'ye cevirmek icin kullanilan carpanlar.</summary>
    public Dictionary<ActivityLevel, decimal> ActivityMultipliers { get; set; } = new()
    {
        [ActivityLevel.Sedentary] = 1.2m,
        [ActivityLevel.LightlyActive] = 1.375m,
        [ActivityLevel.ModeratelyActive] = 1.55m,
        [ActivityLevel.VeryActive] = 1.725m,
        [ActivityLevel.ExtraActive] = 1.9m,
    };

    /// <summary>Aktivite seviyesi belirtilmemisse kullanilacak varsayilan carpan (orta duzey aktif).</summary>
    public decimal DefaultActivityMultiplier { get; set; } = 1.375m;

    /// <summary>Fitness hedefine gore TDEE uzerine eklenen/cikarilan gunluk kalori duzeltmesi.</summary>
    public Dictionary<FitnessGoal, decimal> GoalCalorieAdjustments { get; set; } = new()
    {
        [FitnessGoal.LoseWeight] = -500m,
        [FitnessGoal.BuildMuscle] = 300m,
        [FitnessGoal.GetStronger] = 200m,
        [FitnessGoal.ImproveFitness] = 0m,
        [FitnessGoal.MaintainWeight] = 0m,
        [FitnessGoal.ImproveEndurance] = 0m,
    };

    /// <summary>Gunluk kalori hedefinin inebilecegi minimum guvenli deger.</summary>
    public decimal MinimumDailyCalories { get; set; } = 1200m;

    /// <summary>Gunluk kalorinin protein makrosuna ayrilan yuzdesi.</summary>
    public decimal ProteinCaloriePercentage { get; set; } = 0.30m;

    /// <summary>Gunluk kalorinin karbonhidrat makrosuna ayrilan yuzdesi.</summary>
    public decimal CarbsCaloriePercentage { get; set; } = 0.40m;

    /// <summary>Gunluk kalorinin yag makrosuna ayrilan yuzdesi.</summary>
    public decimal FatCaloriePercentage { get; set; } = 0.30m;

    /// <summary>1 gram proteinin kalori karsiligi.</summary>
    public decimal ProteinCaloriesPerGram { get; set; } = 4m;

    /// <summary>1 gram karbonhidratin kalori karsiligi.</summary>
    public decimal CarbsCaloriesPerGram { get; set; } = 4m;

    /// <summary>1 gram yagin kalori karsiligi.</summary>
    public decimal FatCaloriesPerGram { get; set; } = 9m;

    /// <summary>Kilogram basina onerilen gunluk su miktari (ml).</summary>
    public decimal WaterMlPerKg { get; set; } = 35m;
}
