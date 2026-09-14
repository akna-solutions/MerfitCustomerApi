namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "NutritionPlanGeneration" bolumune karsilik gelen ayarlar.
/// NutritionPlanGenerator'in kullandigi ogun dagilim yuzdeleri ve toleranslar buradan
/// konfigure edilir; kod icinde magic number kullanilmaz.
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
    public int MaxItemsPerMeal { get; set; } = 3;

    /// <summary>Bir ogunde en az kac farkli Food onerilecegi.</summary>
    public int MinItemsPerMeal { get; set; } = 1;

    /// <summary>Toplam gunluk kalorinin hedeften sapabilecegi maksimum oran (orn. 0.05 = %5).</summary>
    public decimal CalorieTolerancePercentage { get; set; } = 0.05m;

    /// <summary>Toplam gunluk proteinin hedeften sapabilecegi maksimum oran (orn. 0.10 = %10).</summary>
    public decimal ProteinTolerancePercentage { get; set; } = 0.10m;

    /// <summary>Bir Food'un porsiyonu, o ogundeki hedef kaloriyi asirtacak sekilde en fazla kac kat buyutulebilir.</summary>
    public decimal MaxPortionMultiplier { get; set; } = 3m;

    /// <summary>Bir Food'un porsiyonu en az ne kadar kucultulebilir (orn. 0.5 = yarim porsiyon).</summary>
    public decimal MinPortionMultiplier { get; set; } = 0.5m;
}
