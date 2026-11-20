using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Activity;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp adim sayar uc noktalari. Kullanici kimligi yalnizca JWT'den okunur; istemci baska bir
/// kullanicinin verisine erisemez. Tarihler cihazin yerel gunudur (yyyy-MM-dd).
/// </summary>
[ApiController]
[Route("api/activity/steps")]
[Authorize]
public class StepActivityController : ControllerBase
{
    private readonly IStepActivityService _service;

    public StepActivityController(IStepActivityService service)
    {
        _service = service;
    }

    /// <summary>Gunluk saatlik adimlari senkronize eder. Idempotent: ayni govde tekrar gonderilirse hicbir sey degismez.</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<StepSyncResponse>> Sync([FromBody] StepSyncRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.SyncAsync(userId, request, cancellationToken));
    }

    /// <summary>Gunluk hedef ve mesafe/kalori katsayilari.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<StepSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetSettingsAsync(userId, cancellationToken));
    }

    /// <summary>Gunluk adim hedefini gunceller (1.000 - 100.000).</summary>
    [HttpPut("goal")]
    public async Task<ActionResult<StepSettingsDto>> UpdateGoal([FromBody] UpdateStepGoalRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.UpdateGoalAsync(userId, request, cancellationToken));
    }

    /// <summary>Tek gun detayi (saatlik adimlar dahil).</summary>
    [HttpGet("days/{date}")]
    public async Task<ActionResult<StepDayDto>> GetDay(DateOnly date, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetDayAsync(userId, date, cancellationToken));
    }

    /// <summary>Tarih araligindaki gunler (en fazla 92 gun).</summary>
    [HttpGet]
    public async Task<ActionResult<StepRangeResponse>> GetRange([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetRangeAsync(userId, from, to, cancellationToken));
    }

    /// <summary>endDate dahil son 7 gunun ozeti ve onceki 7 gunle karsilastirma.</summary>
    [HttpGet("summary/weekly")]
    public async Task<ActionResult<StepWeeklySummaryDto>> GetWeeklySummary([FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _service.GetWeeklySummaryAsync(userId, endDate, cancellationToken));
    }
}
