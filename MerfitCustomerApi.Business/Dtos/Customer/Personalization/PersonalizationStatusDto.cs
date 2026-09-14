namespace MerfitCustomerApi.Business.Dtos.Customer.Personalization;

/// <summary>
/// GET /api/personalization/status yaniti. Kullanicinin kayit sirasinda olusturulan
/// PersonalizationJob'unun guncel durumunu dondurur. Mobil uygulama bu DTO ile
/// Pending/Processing/Completed/Failed durumlarini ayirt edip dashboard'da uygun
/// bir yukleniyor/hazir/hata durumu gosterebilir.
/// </summary>
public class PersonalizationStatusDto
{
    /// <summary>Kullanicinin hic PersonalizationJob kaydi yoksa true (beklenmedik bir durum, savunma amaclidir).</summary>
    public bool HasJob { get; set; }

    /// <summary>"Pending" | "Processing" | "Completed" | "Failed".</summary>
    public string? Status { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Yalnizca Failed durumunda dolu olabilir; stack trace icermeyen kisa bir mesajdir.</summary>
    public string? ErrorMessage { get; set; }
}
