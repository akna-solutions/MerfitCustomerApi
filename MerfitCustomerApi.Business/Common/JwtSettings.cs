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

    /// <summary>Refresh token'in gecerlilik suresi (gun).</summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;
}