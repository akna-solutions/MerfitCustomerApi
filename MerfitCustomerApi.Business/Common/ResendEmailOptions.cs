namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "Resend" bolumu: e-posta saglayicisi (Resend) ayarlari.
///
/// API anahtari buraya YAZILMAZ. Yerelde Visual Studio User Secrets ("Resend:ApiKey"), production'da
/// Resend__ApiKey ortam degiskeni ile verilir. Anahtar yoksa uygulama acilir; yalnizca e-posta gerektiren
/// islemler acik bir yapilandirma hatasiyla (503) reddedilir.
/// </summary>
public class ResendEmailOptions
{
    public const string SectionName = "Resend";

    public const string DefaultFrom = "Mbfit <noreply@mbfit.online>";

    /// <summary>Resend API anahtari (re_...). Kaynak koda ve loglara yazilmaz.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gonderici. Domain'in Resend'de dogrulanmis olmasi gerekir.</summary>
    public string From { get; set; } = DefaultFrom;

    /// <summary>Istege bagli yanit adresi (orn. destek adresi). Bos ise eklenmez.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Gecici hatalarda (ag hatasi, 429, 5xx) toplam deneme sayisi. Ayni idempotency anahtari kullanilir; cift e-posta gitmez.</summary>
    public int MaxSendAttempts { get; set; } = 2;

    /// <summary>Gecici hatadan sonra yeniden denemeden once beklenecek sure.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Tek bir Resend isteginin zaman asimi.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
