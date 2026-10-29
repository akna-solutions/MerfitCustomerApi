using MerfitCustomerApi.Business.Dtos.Customer.Campaigns;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Ana sayfadaki "Senin İçin Önerilenler" kampanyalari. Kampanyalar admin panelinden (MerfitApi) yonetilir;
/// buradan yalnizca su an yayinda olanlar okunur.
/// </summary>
[ApiController]
[Route("api/campaigns")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;

    public CampaignsController(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomerCampaignListItemDto>>> GetActive(CancellationToken cancellationToken)
        => Ok(await _campaignService.GetActiveAsync(cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CustomerCampaignDetailDto>> GetById(long id, CancellationToken cancellationToken)
        => Ok(await _campaignService.GetByIdAsync(id, cancellationToken));
}
