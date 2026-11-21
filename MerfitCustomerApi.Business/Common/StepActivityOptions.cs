namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "StepActivity" bolumu: adim sayar senkronizasyonu, hedef ve makullik (anti-hile) sinirlari.
/// Sinirlara takilan gunler kaydedilir ve kullaniciya gosterilir ama basari/puan hesabina katilmaz
/// (DailyActivity.IsSuspicious).
/// </summary>
public class StepActivityOptions
{
    public const string SectionName = "StepActivity";

    /// <summary>Profilde aktivite seviyesi yoksa kullanilan gunluk adim hedefi.</summary>
    public int DefaultDailyGoal { get; set; } = 10_000;

    /// <summary>Otomatik adim hedefinin alt siniri.</summary>
    public int AutoGoalMin { get; set; } = 5_000;

    /// <summary>Otomatik adim hedefinin ust siniri.</summary>
    public int AutoGoalMax { get; set; } = 12_500;

    public int MinDailyGoal { get; set; } = 1_000;

    public int MaxDailyGoal { get; set; } = 100_000;

    /// <summary>Bugunden geriye en fazla kac gunluk veri kabul edilir (iOS CoreMotion 7 gun saklar).</summary>
    public int MaxBackfillDays { get; set; } = 7;

    /// <summary>Tek istekte gonderilebilecek en fazla gun sayisi.</summary>
    public int MaxDaysPerSync { get; set; } = 8;

    /// <summary>Bir saatte makul en fazla adim (60 dk boyunca ~200 adim/dk kosu).</summary>
    public int MaxStepsPerHour { get; set; } = 12_000;

    /// <summary>Bir gunde makul en fazla adim.</summary>
    public int MaxStepsPerDay { get; set; } = 60_000;

    /// <summary>Icinde bulunulan saat icin dakika basina en fazla adim (saatin gecen dakikasiyla carpilir).</summary>
    public int MaxStepsPerMinute { get; set; } = 250;

    /// <summary>Cihaz ile sunucu saati arasindaki kabul edilen fark (dakika).</summary>
    public int ClockToleranceMinutes { get; set; } = 5;

    /// <summary>Tarih araligi sorgusunda en fazla gun.</summary>
    public int MaxRangeDays { get; set; } = 92;

    /// <summary>Profilde boy yoksa kullanilan adim uzunlugu (metre).</summary>
    public double DefaultStrideMeters { get; set; } = 0.762;

    /// <summary>Profilde kilo yoksa kullanilan kilo (kg).</summary>
    public double DefaultWeightKg { get; set; } = 70;

    /// <summary>Yuruyusun net enerji maliyeti (kcal / kg / km). Tahmini kalori = km * kg * bu deger.</summary>
    public double WalkingKcalPerKgPerKm { get; set; } = 0.5;
}
