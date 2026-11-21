using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Energy;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Gunluk kalori dengesi (alinan - harcanan). Kullanici kimligi yalnizca JWT'den okunur. Tarihler kullanicinin yerel
/// gunudur (yyyy-MM-dd). Hesaplama tamamen sunucudadir; uc noktalar hicbir sey yazmaz.
/// </summary>
[ApiController]
[Route("api/energy-balance")]
[Authorize]
public class EnergyBalanceController : ControllerBase
{
    private readonly IEnergyBalanceService _service;

    public EnergyBalanceController(IEnergyBalanceService service)
    {
        _service = service;
    }

    /// <summary>Tek gunun dengesi, harcama dagilimi, beslenme/aktivite analizi ve yorumlar. date verilmezse bugun.</summary>
    [HttpGet("day")]
    public async Task<ActionResult<EnergyDayDto>> GetDay([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetDayAsync(userId, date, cancellationToken));
    }

    /// <summary>Aralik (en fazla 92 gun): gunluk seri, ortalamalar, haftalik ortalama acik/fazla ve kilo degisimi.</summary>
    [HttpGet]
    public async Task<ActionResult<EnergyRangeResponse>> GetRange([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetRangeAsync(userId, from, to, cancellationToken));
    }
}
