namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "AccountEmail" bolumu: e-posta dogrulama ve sifre sifirlama akislarinin
/// kod sureleri, deneme ve tekrar gonderim sinirlari.
/// </summary>
public class AccountEmailOptions
{
    public const string SectionName = "AccountEmail";

    /// <summary>E-posta dogrulama kodunun gecerlilik suresi.</summary>
    public TimeSpan EmailVerificationTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sifre sifirlama kodunun gecerlilik suresi.</summary>
    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Bir koda yapilabilecek en fazla deneme; asilinca kod gecersiz olur ve yeni kod istenir.</summary>
    public int MaxCodeAttempts { get; set; } = 5;

    /// <summary>Ayni hesaba ayni turde iki e-posta arasinda beklenmesi gereken en kisa sure.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Hesap basina, tur basina saatte gonderilebilecek en fazla e-posta.</summary>
    public int MaxEmailsPerHour { get; set; } = 5;

    /// <summary>Hassas auth uclarina IP basina pencere icinde izin verilen istek sayisi (0: sinir yok).</summary>
    public int PerIpRequestsPerWindow { get; set; } = 20;

    /// <summary>Kullanici adi anlik kontrolune IP basina pencere icinde izin verilen istek sayisi (0: sinir yok).</summary>
    public int UsernameCheckRequestsPerWindow { get; set; } = 120;

    /// <summary>IP siniri penceresi.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(15);
}
