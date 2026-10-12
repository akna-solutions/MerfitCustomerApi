namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// INutritionCalculator tarafindan uretilen, saf (entity'den bagimsiz) beslenme hedefi
/// hesaplama sonucu. AuthService, NutritionService ve plan olusturucu bu sonucu NutritionGoal
/// entity'sine esler. Makrolarin enerjisi (4/4/9 kcal) gunluk kaloriyle tutarlidir; fark yalnizca
/// gram yuvarlamasindan kaynaklanir.
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

    /// <summary>Mifflin-St Jeor bazal metabolizma hizi (kcal).</summary>
    public decimal Bmr { get; set; }

    /// <summary>Kullanilan aktivite carpani.</summary>
    public decimal ActivityMultiplier { get; set; }

    /// <summary>Toplam gunluk enerji harcamasi (BMR x aktivite carpani).</summary>
    public decimal Tdee { get; set; }

    /// <summary>Hedefe gore uygulanan kalori duzeltmesi (kcal; negatif = acik).</summary>
    public decimal GoalCalorieAdjustment { get; set; }

    /// <summary>Hesaplamaya dair kullaniciya gosterilebilecek aciklamalar (orn. hedef kiloya gore acik uygulanmadi).</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Kisisel plan uretimi icin kati degerlendirme sonucu. Gerekli profil verisi eksik ya da
/// gecersizse <see cref="Result"/> null'dir ve <see cref="Issues"/> neyin duzeltilmesi gerektigini
/// kullanicinin anlayacagi dille listeler.
/// </summary>
public class NutritionTargetEvaluation
{
    /// <summary>Tum girdiler gecerliyse hesaplanan hedefler; aksi halde null.</summary>
    public NutritionCalculationResult? Result { get; init; }

    /// <summary>Eksik veya gecersiz profil alanlari.</summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>Hedefler hesaplanabildi mi.</summary>
    public bool IsComplete => Result is not null && Issues.Count == 0;
}
