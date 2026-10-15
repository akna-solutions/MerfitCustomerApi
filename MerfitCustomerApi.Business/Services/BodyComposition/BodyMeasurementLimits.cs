namespace MerfitCustomerApi.Business.Services.BodyComposition;

/// <summary>
/// Vucut olcumlerinin kabul edilen araliklari (metrik birim). API dogrulamasi (DTO [Range] nitelikleri)
/// ve mobil form ayni sinirlari kullanir; aralik disindaki deger kaydedilmez.
/// </summary>
public static class BodyMeasurementLimits
{
    public const double MinWeightKg = 20;
    public const double MaxWeightKg = 400;

    public const double MinBodyFatPercentage = 2;
    public const double MaxBodyFatPercentage = 75;

    public const double MinNeckCm = 20;
    public const double MaxNeckCm = 80;

    public const double MinChestCm = 50;
    public const double MaxChestCm = 250;

    public const double MinWaistCm = 40;
    public const double MaxWaistCm = 250;

    public const double MinHipCm = 50;
    public const double MaxHipCm = 250;

    public const double MinArmCm = 15;
    public const double MaxArmCm = 80;

    public const double MinThighCm = 25;
    public const double MaxThighCm = 150;
}
