using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// MB Shop urun kategorisi (store.Category). Admin panelinden (MerfitApi) yonetilir; musteri API'si yalnizca aktifleri okur.
/// </summary>
public class StoreCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>URL dostu benzersiz anahtar (orn. "protein-ve-takviyeler").</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Gorsel yuklenemediginde mobilde gosterilen Ionicons adi (orn. "barbell-outline").</summary>
    public string? Icon { get; set; }

    /// <summary>Ust kategori (hiyerarsi gerekirse). Ilk surumde mobil tek seviye gosterir.</summary>
    public long? ParentCategoryId { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
