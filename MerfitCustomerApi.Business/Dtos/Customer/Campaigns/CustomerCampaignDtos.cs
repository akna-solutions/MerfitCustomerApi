namespace MerfitCustomerApi.Business.Dtos.Customer.Campaigns;

/// <summary>Ana sayfadaki kampanya karti (GET /api/campaigns).</summary>
public class CustomerCampaignListItemDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Kartta gosterilen kisa aciklama (AppContent.Description).</summary>
    public string? Summary { get; set; }
    public string? ImageUrl { get; set; }
    public string? Category { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
}

/// <summary>Kampanya detay ekrani (GET /api/campaigns/{id}).</summary>
public class CustomerCampaignDetailDto : CustomerCampaignListItemDto
{
    /// <summary>Detayli aciklama (duz metin); girilmediyse null.</summary>
    public string? Body { get; set; }

    /// <summary>Istege bagli hedef baglanti (https).</summary>
    public string? LinkUrl { get; set; }
}
