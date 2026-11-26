using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.BodyComposition;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Vucut olcumleri (kilo, yag orani, boyun/gogus/bel/kalca/kol/uyluk cevresi) ve yag orani degerlendirmesi.
/// MerfitNativeApp Profil > Vücut Ölçümleri ekrani bu ucu kullanir.
/// </summary>
[ApiController]
[Route("api/body-measurements")]
[Authorize]
public class BodyMeasurementsController : ControllerBase
{
    private readonly IBodyCompositionService _bodyCompositionService;

    public BodyMeasurementsController(IBodyCompositionService bodyCompositionService)
    {
        _bodyCompositionService = bodyCompositionService;
    }

    /// <summary>
    /// Olcum gecmisi (yeniden eskiye) ve guncel yag orani degerlendirmesi. <paramref name="limit"/> en yeni kac olcumun
    /// donecegini belirler (varsayilan 20, en fazla 200); toplam sayi totalCount/hasMore alanlarindadir.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BodyMeasurementHistoryResponse>> GetHistory([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _bodyCompositionService.GetHistoryAsync(userId, limit, cancellationToken));
    }

    /// <summary>Yeni olcum ekler (eski kayitlar korunur). Govde metrik birimdedir: { "waistCm": 82, "neckCm": 38 }.</summary>
    [HttpPost]
    public async Task<ActionResult<BodyMeasurementHistoryResponse>> AddMeasurement(
        [FromBody] CreateBodyMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _bodyCompositionService.AddMeasurementAsync(userId, request, cancellationToken));
    }
}
