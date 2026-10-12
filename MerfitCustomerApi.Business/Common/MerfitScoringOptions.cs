namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "MerfitScoring" bolumune karsilik gelen MB FIT puan kurallari.
/// Puanlar kaynak verilerden (tamamlanan antrenman, kisisel rekor, ogun/su kaydi, kazanilan basari)
/// her istekte yeniden turetilir; bu degerler degistiginde gecmis puanlar da yeni kurala gore hesaplanir.
/// MerfitApi (admin) ayni bolumu ve ayni varsayilanlari kullanir; iki tarafta da ayni tutulmalidir.
/// </summary>
public class MerfitScoringOptions
{
    public const string SectionName = "MerfitScoring";

    /// <summary>Tamamlanan her antrenman oturumu icin verilen puan.</summary>
    public int WorkoutCompletedPoints { get; set; } = 100;

    /// <summary>Bir gunde puan getiren en fazla antrenman sayisi (puan kasilmasini onler).</summary>
    public int MaxScoredWorkoutsPerDay { get; set; } = 2;

    /// <summary>Kirilan her kisisel rekor icin verilen puan.</summary>
    public int PersonalRecordPoints { get; set; } = 25;

    /// <summary>Ogun kaydi girilen her gun icin verilen puan (gunde bir kez).</summary>
    public int MealLogDayPoints { get; set; } = 10;

    /// <summary>Su kaydi girilen her gun icin verilen puan (gunde bir kez).</summary>
    public int WaterLogDayPoints { get; set; } = 5;

    /// <summary>
    /// Gun/hafta/ay sinirlarinin hesaplandigi saat dilimi farki (Turkiye icin UTC+3).
    /// Haftalik siralama Pazartesi 00:00, aylik siralama ayin 1'i 00:00 yerel saatte baslar.
    /// </summary>
    public int TimeZoneOffsetHours { get; set; } = 3;
}
