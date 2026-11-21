namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Gunluk kalori dengesi (alinan - harcanan) hesap ve yorum esikleri. BMR katsayilari ayrica tutulmaz;
/// beslenme hedefiyle ayni olsun diye <see cref="NutritionCalculationOptions"/> (Mifflin-St Jeor) kullanilir.
/// Adim kalorisi <see cref="StepActivityOptions"/> ile sunucuda zaten hesaplanmis olan DailyActivity.CaloriesBurned'dur.
/// </summary>
public class EnergyBalanceOptions
{
    public const string SectionName = "EnergyBalance";

    /// <summary>Besinlerin termik etkisi: alinan kalorinin bu orani harcamaya eklenir (yaygin kabul ~%10).</summary>
    public decimal ThermicEffectRatio { get; set; } = 0.10m;

    /// <summary>
    /// Bir saatlik kovada (antrenmanla cakisan adimlar dusuldukten sonra) en az bu kadar adim varsa o saatin adimlari
    /// "yuruyus", altindakiler "gunluk hareketlilik" sayilir. 2.500 adim/saat ~ 25 dk kesintisiz yuruyuse karsilik gelir.
    /// Bu yalnizca gosterim ayrimidir; toplam adim kalorisi degismez.
    /// </summary>
    public int WalkingHourStepThreshold { get; set; } = 2500;

    /// <summary>Dengenin "sifira yakin" sayildigi tolerans (kcal).</summary>
    public int BalanceToleranceKcal { get; set; } = 150;

    /// <summary>Kilo koruma hedefinde dengenin uyumlu sayildigi bant (+/- kcal).</summary>
    public int MaintainBandKcal { get; set; } = 250;

    /// <summary>Bu tutardan buyuk gunluk acik surdurulebilir sayilmaz ve dikkat (kirmizi) olarak gosterilir.</summary>
    public int MaxSustainableDeficitKcal { get; set; } = 1000;

    /// <summary>Kilo alma hedefinde bu tutardan buyuk fazla "kontrollu" sayilmaz (turuncu).</summary>
    public int MaxControlledSurplusKcal { get; set; } = 750;

    /// <summary>Protein bu oranin altinda kalirsa (tamamlanmis gunde) yorum uretilir.</summary>
    public decimal LowProteinRatio { get; set; } = 0.80m;

    /// <summary>Adim verisi bundan eski senkronize edildiyse bugun icin "senkronizasyon gecikmis" uyarisi verilir.</summary>
    public int StepSyncStaleMinutes { get; set; } = 120;

    /// <summary>Tek antrenman oturumunun kaloriye katilan en uzun suresi (dakika); daha uzun sureler kirpilir.</summary>
    public int MaxWorkoutMinutes { get; set; } = 240;

    /// <summary>Tek istekte istenebilecek en uzun aralik (gun).</summary>
    public int MaxRangeDays { get; set; } = 92;

    /// <summary>1 kg vucut agirligi degisimine karsilik gelen yaklasik enerji (kcal). Yalnizca tahmini gosterim icindir.</summary>
    public int KcalPerKg { get; set; } = 7700;
}
