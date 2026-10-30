namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "Jwt" bolumune karsilik gelen ayarlar.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>Token imzalamada kullanilan simetrik gizli anahtar (en az 32 karakter).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Token'i yayinlayan taraf.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token'in gecerli oldugu hedef kitle.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Access token'in gecerlilik suresi (dakika).</summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token'in gecerlilik suresi (gun). Kayan suredir: her yenilemede (POST /api/auth/refresh) yeni token
    /// bu kadar gun gecerli uretilir. Boylece uygulamayi kullanan kullanicinin oturumu acik kalir; bu kadar gun
    /// ust uste hic girmeyen kullanicinin oturumu kapanir.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 10;

    /// <summary>
    /// Yenilenmis (kullanilmis) bir refresh token'in bu kadar saniye daha kabul edilmesi. Yenileme yaniti mobil
    /// agda kaybolursa uygulama eski token'la tekrar dener; bu pencere o durumda kullanicinin cikis yapmasini onler.
    /// </summary>
    public int RefreshTokenReuseGraceSeconds { get; set; } = 60;
}