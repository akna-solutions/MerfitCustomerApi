namespace MerfitCustomerApi.Domain.Common;

/// <summary>
/// Tum entity'lerin turedigi temel sinif; ortak kimlik ve denetim (audit) alanlarini icerir.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Kaydin benzersiz kimligi.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Kaydin olusturulma tarihi.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Kaydin son guncellenme tarihi.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Kaydi olusturan kullanicinin kimligi.
    /// </summary>
    public long? CreatedUser { get; set; }

    /// <summary>
    /// Kaydi son guncelleyen kullanicinin kimligi.
    /// </summary>
    public long? UpdatedUser { get; set; }
}
