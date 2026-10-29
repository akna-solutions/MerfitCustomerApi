using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// MB Shop'taki "Indirimler ve Kampanyalar" karti (store.Campaign). Ana sayfa kampanyalarindan (content.AppContent)
/// ayridir: bir urun grubunu listeler. Indirim orani urun fiyatlarindan turetilir, ayrica saklanmaz.
/// </summary>
public class StoreCampaign : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Gorsel yokken kart zemin rengi (#RRGGBB).</summary>
    public string? Tint { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public List<StoreCampaignProduct> Products { get; set; } = new();
}
