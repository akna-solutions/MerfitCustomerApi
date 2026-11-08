namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "WorkoutPlanLab" bolumu: Antrenman Laboratuvari simulasyon kuyrugu ve kalite kontrol esikleri.
/// Esikler laboratuvarin kendi aciklanmis kurallaridir; bilimsel olarak dogrulanmis kalite olcutleri degildir.
/// </summary>
public class WorkoutPlanLabOptions
{
    public const string SectionName = "WorkoutPlanLab";

    /// <summary>Kuyruk isleyicisi calissin mi (false ise admin panelindeki testler Pending'de kalir).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bekleyen test yokken kuyrugun kontrol araligi.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Processing'de bu sureden uzun kalan test (orn. uygulama yeniden basladi) tekrar denenir.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxAttemptCount { get; set; } = 2;

    /// <summary>Bir gunde bundan az egzersiz varsa "Yeterli egzersiz" kontrolu uyari verir.</summary>
    public int MinExercisesPerDay { get; set; } = 4;

    /// <summary>Ana antrenmanlarin ortalama hedef uyum puani (FitScore) bu degere esit/ustundeyse basarili.</summary>
    public decimal GoalFitPassThreshold { get; set; } = 60;

    /// <summary>Ortalama FitScore bu degerin altindaysa basarisiz; iki esik arasi uyari.</summary>
    public decimal GoalFitWarningThreshold { get; set; } = 40;
}
