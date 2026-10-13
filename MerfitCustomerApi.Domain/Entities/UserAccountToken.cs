using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama baglantilarindaki tek kullanimlik, sureli jeton.
/// Jetonun kendisi hicbir yerde saklanmaz; yalnizca SHA-256 ozeti (<see cref="TokenHash"/>) tutulur.
/// Veritabani okunsa bile jetonla islem yapilamaz.
/// </summary>
public class UserAccountToken : BaseEntity
{
    /// <summary>Jetonun ait oldugu kullanici.</summary>
    public long UserId { get; set; }

    /// <summary>Jetonun amaci (dogrulama / sifre sifirlama).</summary>
    public AccountTokenPurpose Purpose { get; set; }

    /// <summary>Jetonun SHA-256 ozeti (64 karakter, kucuk harf hex). Benzersizdir.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Jetonun gonderildigi e-posta (normallestirilmis). Kullanici e-postasini degistirirse eski adrese
    /// gonderilen jeton gecersiz olur; eski adres yeni adresi dogrulayamaz.
    /// </summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>Bu andan sonra jeton kullanilamaz.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Jetonun kullanildigi an. Iyimser eszamanlilik belirtecidir: ayni jetonu ayni anda kullanmaya
    /// calisan iki istekten yalnizca biri basarili olur.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Yeni jeton uretildiginde veya sifre degistiginde jetonun iptal edildigi an.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Jetonu isteyen istemcinin IP adresi (denetim icin).</summary>
    public string? RequestedByIp { get; set; }
}
