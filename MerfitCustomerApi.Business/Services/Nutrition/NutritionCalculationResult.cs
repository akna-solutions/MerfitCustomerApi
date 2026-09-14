namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// INutritionCalculator tarafindan uretilen, saf (entity'den bagimsiz) beslenme hedefi
/// hesaplama sonucu. AuthService ve NutritionService bu sonucu kendi NutritionGoal
/// entity'lerine esler.
/// </summary>
public class NutritionCalculationResult
{
    /// <summary>Gunluk hedeflenen kalori miktari.</summary>
    public decimal DailyCalories { get; set; }

    /// <summary>Gunluk hedeflenen protein miktari (gram).</summary>
    public decimal ProteinTarget { get; set; }

    /// <summary>Gunluk hedeflenen karbonhidrat miktari (gram).</summary>
    public decimal CarbsTarget { get; set; }

    /// <summary>Gunluk hedeflenen yag miktari (gram).</summary>
    public decimal FatTarget { get; set; }

    /// <summary>Gunluk hedeflenen su miktari (mililitre).</summary>
    public decimal WaterTargetMl { get; set; }
}
