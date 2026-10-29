using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Kampanya-urun iliskisi (store.CampaignProduct); (CampaignId, ProductId) benzersizdir.</summary>
public class StoreCampaignProduct : BaseEntity
{
    public long CampaignId { get; set; }

    public long ProductId { get; set; }

    public int SortOrder { get; set; }
}
