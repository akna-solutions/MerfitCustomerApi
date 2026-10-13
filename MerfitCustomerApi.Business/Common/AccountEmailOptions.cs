namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "AccountEmail" bolumu: e-posta dogrulama ve sifre sifirlama akislarinin
/// baglanti adresleri, jeton sureleri ve tekrar gonderim sinirlari.
/// </summary>
public class AccountEmailOptions
{
    public const string SectionName = "AccountEmail";

    /// <summary>
    /// E-postadaki baglantilarin koku. Baglantilar {PublicBaseUrl}/verify-email#token=... ve
    /// {PublicBaseUrl}/reset-password#token=... seklindedir; bu iki yol API'deki AccountLinksController
    /// tarafindan sunulur (jeton "#" sonrasinda oldugu icin sunucu/proxy loglarina dusmez).
    /// </summary>
    public string PublicBaseUrl { get; set; } = "https://mbfit.online";

    /// <summary>Mobil uygulamanin deep link semasi (MerfitNativeApp app.json "scheme").</summary>
    public string AppScheme { get; set; } = "mbfitnativeapp";

    /// <summary>E-posta dogrulama baglantisinin gecerlilik suresi.</summary>
    public TimeSpan EmailVerificationTokenLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Sifre sifirlama baglantisinin gecerlilik suresi.</summary>
    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Ayni hesaba ayni turde iki e-posta arasinda beklenmesi gereken en kisa sure.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Hesap basina, tur basina saatte gonderilebilecek en fazla e-posta.</summary>
    public int MaxEmailsPerHour { get; set; } = 5;

    /// <summary>Hassas auth uclarina IP basina pencere icinde izin verilen istek sayisi (0: sinir yok).</summary>
    public int PerIpRequestsPerWindow { get; set; } = 20;

    /// <summary>IP siniri penceresi.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(15);
}
