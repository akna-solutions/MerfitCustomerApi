namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "ProgressiveOverload" bolumu: cift progresyon (double progression) onerisinin
/// katsayilari. Kullanici bir egzersizde son tamamlanan antrenmaninda hedef set sayisinin tamaminda
/// tekrar araliginin ust sinirina ulastiysa kucuk bir agirlik artisi onerilir; aksi halde agirlik korunur.
/// </summary>
public class ProgressiveOverloadOptions
{
    public const string SectionName = "ProgressiveOverload";

    /// <summary>Calisma agirliginin yuzdesi olarak onerilen artis.</summary>
    public decimal IncreasePercent { get; set; } = 5m;

    /// <summary>En kucuk artis (kg).</summary>
    public decimal MinIncreaseKg { get; set; } = 1m;

    /// <summary>En buyuk artis (kg); agir egzersizlerde bile artis kucuk tutulur.</summary>
    public decimal MaxIncreaseKg { get; set; } = 2.5m;

    /// <summary>Onerilen agirligin yuvarlanacagi adim (kg), orn. 0.5.</summary>
    public decimal RoundingStepKg { get; set; } = 0.5m;

    /// <summary>
    /// Setlerde RPE girildiyse, artis icin her setin RPE'si bu degeri asmamalidir (tukenise cok yakin
    /// setler artis gerekcesi sayilmaz). RPE girilmemisse bu kural uygulanmaz.
    /// </summary>
    public decimal MaxRpeForIncrease { get; set; } = 9.5m;
}
