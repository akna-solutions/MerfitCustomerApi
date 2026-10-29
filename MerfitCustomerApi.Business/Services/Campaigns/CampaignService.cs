using MerfitCustomerApi.Business.Dtos.Customer.Campaigns;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Campaigns;

/// <summary>
/// ICampaignService'in varsayilan implementasyonu. Kampanyalar mevcut content.AppContent tablosunda
/// Type = "Campaign" olarak tutulur (MerfitApi admin uclari ile yonetilir); ayri tablo yoktur.
/// Yayinda olma kurali admin panelindeki "IsCurrentlyLive" ile aynidir: aktif VE (baslangic yok ya da gecti)
/// VE (bitis yok ya da gelmedi).
/// </summary>
public class CampaignService : ICampaignService
{
    public const string CampaignType = "Campaign";

    /// <summary>Ana sayfada gosterilecek en fazla kampanya sayisi.</summary>
    public const int MaxListCount = 20;

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CampaignService(IUnitOfWork unitOfWork, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<List<CustomerCampaignListItemDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await LiveQuery()
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.StartAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(MaxListCount)
            .ToListAsync(cancellationToken);

        return rows.Select(c =>
        {
            var dto = new CustomerCampaignListItemDto();
            Fill(dto, c);
            return dto;
        }).ToList();
    }

    public async Task<CustomerCampaignDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var campaign = await LiveQuery().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Kampanya bulunamadi veya artik yayinda degil.");

        var dto = new CustomerCampaignDetailDto
        {
            Body = string.IsNullOrWhiteSpace(campaign.Body) ? null : campaign.Body,
            LinkUrl = IsSafeLink(campaign.LinkUrl) ? campaign.LinkUrl!.Trim() : null,
        };
        Fill(dto, campaign);
        return dto;
    }

    private IQueryable<AppContent> LiveQuery()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return _unitOfWork.Repository<AppContent>()
            .GetQueryable()
            .Where(c => c.Type == CampaignType
                && c.IsActive
                && c.Title != null && c.Title != ""
                && (c.StartAt == null || c.StartAt <= now)
                && (c.EndAt == null || c.EndAt >= now));
    }

    private static void Fill(CustomerCampaignListItemDto dto, AppContent c)
    {
        dto.Id = c.Id;
        dto.Title = c.Title ?? string.Empty;
        dto.Summary = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description;
        dto.ImageUrl = IsSafeLink(c.ImageUrl) ? c.ImageUrl!.Trim() : null;
        dto.Category = string.IsNullOrWhiteSpace(c.Category) ? null : c.Category;
        dto.StartAt = c.StartAt is null ? null : DateTime.SpecifyKind(c.StartAt.Value, DateTimeKind.Utc);
        dto.EndAt = c.EndAt is null ? null : DateTime.SpecifyKind(c.EndAt.Value, DateTimeKind.Utc);
    }

    /// <summary>Mobilde acilacak adres yalnizca http(s) olabilir (javascript:, intent: vb. elenir).</summary>
    private static bool IsSafeLink(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
