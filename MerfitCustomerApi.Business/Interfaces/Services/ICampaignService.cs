using MerfitCustomerApi.Business.Dtos.Customer.Campaigns;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Admin panelinden yonetilen kampanyalarin (AppContent, Type = "Campaign") mobil uygulamaya sunulmasi.</summary>
public interface ICampaignService
{
    /// <summary>Su an yayinda olan (aktif ve tarih araliginda) kampanyalar, siralama onceligine gore.</summary>
    Task<List<CustomerCampaignListItemDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Yayindaki bir kampanyanin detayi; pasif, suresi dolmus veya baska turdeki icerik icin NotFound.</summary>
    Task<CustomerCampaignDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
