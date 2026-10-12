using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "NutritionPlanGeneration" bolumune karsilik gelen ayarlar.
/// NutritionPlanGenerator'in kullandigi ogun dagilim yuzdeleri, porsiyon sinirlari, toleranslar,
/// cesitlilik cezalari ve hedefe gore besin puanlama agirliklari buradan konfigure edilir; kod icinde
/// magic number kullanilmaz.
/// </summary>
public class NutritionPlanGenerationOptions
{
    public const string SectionName = "NutritionPlanGeneration";

    /// <summary>
    /// Gunluk kalori/makro hedefinin ogunler arasinda nasil dagitilacagi (toplam 1.0 olmali).
    /// Sirasiyla Breakfast/Lunch/Dinner/Snack.
    /// </summary>
    public decimal BreakfastCaloriePercentage { get; set; } = 0.25m;
    public decimal LunchCaloriePercentage { get; set; } = 0.35m;
    public decimal DinnerCaloriePercentage { get; set; } = 0.30m;
    public decimal SnackCaloriePercentage { get; set; } = 0.10m;

    /// <summary>Bir ogunde en fazla kac farkli Food onerilecegi.</summary>
    public int MaxItemsPerMeal { get; set; } = 4;

    /// <summary>Bir ogunde en az kac farkli Food onerilecegi.</summary>
    public int MinItemsPerMeal { get; set; } = 1;

    /// <summary>Toplam gunluk kalorinin hedeften sapabilecegi maksimum oran (orn. 0.05 = %5).</summary>
    public decimal CalorieTolerancePercentage { get; set; } = 0.05m;

    /// <summary>Toplam gunluk proteinin hedeften sapabilecegi maksimum oran (orn. 0.10 = %10).</summary>
    public decimal ProteinTolerancePercentage { get; set; } = 0.10m;

    /// <summary>Toplam gunluk karbonhidratin hedeften sapabilecegi maksimum oran.</summary>
    public decimal CarbsTolerancePercentage { get; set; } = 0.15m;

    /// <summary>Toplam gunluk yagin hedeften sapabilecegi maksimum oran.</summary>
    public decimal FatTolerancePercentage { get; set; } = 0.15m;

    /// <summary>Bir besinin porsiyonu referans porsiyonun en fazla kac kati olabilir.</summary>
    public decimal MaxPortionMultiplier { get; set; } = 3m;

    /// <summary>Bir besinin porsiyonu referans porsiyonun en az kac kati olabilir (orn. 0.5 = yarim porsiyon).</summary>
    public decimal MinPortionMultiplier { get; set; } = 0.5m;

    /// <summary>Gram/ml gibi bolunebilir birimlerde denenen porsiyon katsayisi adimi.</summary>
    public decimal PortionMultiplierStep { get; set; } = 0.25m;

    /// <summary>Gram/ml gibi bolunebilir birimlerde miktarin yuvarlandigi adim (orn. 5 g).</summary>
    public decimal ContinuousQuantityRounding { get; set; } = 5m;

    /// <summary>Adet/dilim gibi sayilabilir birimlerde miktar adimi (orn. 0.5 = yarim adet).</summary>
    public decimal DiscreteQuantityStep { get; set; } = 0.5m;

    /// <summary>
    /// Bolunebilir (gram/ml cinsinden tartilabilir) kabul edilen porsiyon birimleri; buyuk/kucuk harf
    /// duyarsiz. Listede olmayan birimler (adet, dilim, kase...) sayilabilir kabul edilir.
    /// </summary>
    public List<string> ContinuousServingUnits { get; set; } = new() { "g", "gr", "gram", "ml", "mL" };

    /// <summary>Porsiyon cozumunde ogun kalori hatasinin agirligi.</summary>
    public decimal CalorieErrorWeight { get; set; } = 3m;

    /// <summary>Porsiyon cozumunde ogun protein hatasinin agirligi.</summary>
    public decimal ProteinErrorWeight { get; set; } = 2m;

    /// <summary>Porsiyon cozumunde ogun karbonhidrat hatasinin agirligi.</summary>
    public decimal CarbsErrorWeight { get; set; } = 1m;

    /// <summary>Porsiyon cozumunde ogun yag hatasinin agirligi.</summary>
    public decimal FatErrorWeight { get; set; } = 1m;

    /// <summary>
    /// Gun seviyesi iyilestirmede bir ogunun kalorisinin kendi hedefinden en fazla ne kadar
    /// sapabilecegi (ogun dagilimini korumak icin).
    /// </summary>
    public decimal MealCalorieFlexPercentage { get; set; } = 0.35m;

    /// <summary>Gun seviyesi porsiyon iyilestirmesinin en fazla kac tur yapilacagi.</summary>
    public int MaxDayRefinementPasses { get; set; } = 4;

    /// <summary>Ayni ogun turunde bir onceki gun kullanilan besine uygulanan ceza.</summary>
    public decimal PreviousDaySameMealPenalty { get; set; } = 3m;

    /// <summary>Bir onceki gun herhangi bir ogunde kullanilan besine uygulanan ceza.</summary>
    public decimal PreviousDayAnyMealPenalty { get; set; } = 1m;

    /// <summary>Besinin hafta icinde daha once her kullanilisi icin uygulanan ceza.</summary>
    public decimal WeeklyUsagePenalty { get; set; } = 0.5m;

    /// <summary>
    /// Kullaniciya/gune ozel deterministik cesitlilik katsayisinin agirligi (esit puanli besinler arasinda
    /// her kullaniciya farkli ama tekrarlanabilir secim yapar).
    /// </summary>
    public decimal VarietyJitterWeight { get; set; } = 0.3m;

    /// <summary>
    /// Bir besinin bildirilen kalorisi ile makrolarindan hesaplanan enerji (4/4/9) arasindaki izin verilen
    /// en buyuk oransal fark. Asan kayitlar tutarsiz veri kabul edilip plana alinmaz.
    /// </summary>
    public decimal EnergyConsistencyTolerance { get; set; } = 0.35m;

    /// <summary>Dusuk kalorili besinlerde enerji tutarlilik kontrolu icin mutlak tolerans (kcal).</summary>
    public decimal EnergyConsistencyMinimumKcal { get; set; } = 15m;

    /// <summary>Hedefe gore besin secim agirliklari.</summary>
    public Dictionary<FitnessGoal, FoodScoringWeights> GoalScoringWeights { get; set; } = new()
    {
        // Kilo verme: proteini ve lifi yuksek, enerji yogunlugu ve sekeri dusuk besinler.
        [FitnessGoal.LoseWeight] = new() { ProteinShare = 2m, CarbsShare = 0m, FatShare = -1m, Fiber = 1.5m, SugarShare = -1m, EnergyDensity = -1.5m },
        // Kas kazanimi: protein ve karbonhidrati yuksek, enerji yogun besinler.
        [FitnessGoal.BuildMuscle] = new() { ProteinShare = 2m, CarbsShare = 1m, FatShare = 0m, Fiber = 0.3m, SugarShare = -0.3m, EnergyDensity = 0.5m },
        [FitnessGoal.GetStronger] = new() { ProteinShare = 2m, CarbsShare = 0.8m, FatShare = 0m, Fiber = 0.3m, SugarShare = -0.3m, EnergyDensity = 0.3m },
        // Dayaniklilik: karbonhidrat agirlikli.
        [FitnessGoal.ImproveEndurance] = new() { ProteinShare = 1m, CarbsShare = 2m, FatShare = -0.3m, Fiber = 0.5m, SugarShare = 0m, EnergyDensity = 0m },
        [FitnessGoal.MaintainWeight] = new() { ProteinShare = 1m, CarbsShare = 0.5m, FatShare = -0.3m, Fiber = 1m, SugarShare = -0.5m, EnergyDensity = -0.3m },
        [FitnessGoal.ImproveFitness] = new() { ProteinShare = 1.2m, CarbsShare = 0.7m, FatShare = -0.3m, Fiber = 1m, SugarShare = -0.5m, EnergyDensity = -0.5m },
    };

    /// <summary>
    /// Protein kaynagi slotunda adaylarin protein enerji payina, hedeften bagimsiz eklenen agirlik
    /// (slotun amaci protein saglamaktir).
    /// </summary>
    public decimal ProteinSlotFocusWeight { get; set; } = 3m;

    /// <summary>Lif puaninin normalize edildigi deger (100 kcal basina gram); bunun ustu tam puan alir.</summary>
    public decimal FiberPer100KcalForFullScore { get; set; } = 5m;

    /// <summary>Enerji yogunlugu puaninin normalize edildigi deger (kcal/g); bunun ustu tam puan alir.</summary>
    public decimal EnergyDensityKcalPerGramForFullScore { get; set; } = 4m;
}

/// <summary>
/// Bir besinin hedefe uygunluk puanini olusturan ozelliklerin agirliklari. Pozitif agirlik ozelligi
/// odullendirir, negatif agirlik cezalandirir. Ozellikler 0-1 araligina normalize edilir.
/// </summary>
public class FoodScoringWeights
{
    /// <summary>Enerjinin proteinden gelen payi.</summary>
    public decimal ProteinShare { get; set; }

    /// <summary>Enerjinin karbonhidrattan gelen payi.</summary>
    public decimal CarbsShare { get; set; }

    /// <summary>Enerjinin yagdan gelen payi.</summary>
    public decimal FatShare { get; set; }

    /// <summary>100 kcal basina lif (bilinmiyorsa 0 kabul edilir; odul verilmez).</summary>
    public decimal Fiber { get; set; }

    /// <summary>Enerjinin sekerden gelen payi (bilinmiyorsa 0).</summary>
    public decimal SugarShare { get; set; }

    /// <summary>Gram/ml basina kalori (yalnizca bolunebilir birimlerde hesaplanir).</summary>
    public decimal EnergyDensity { get; set; }
}
