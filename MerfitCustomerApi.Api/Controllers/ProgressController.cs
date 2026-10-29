using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Calendar;
using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>MerfitNativeApp ProgressScreen'inin ihtiyac duydugu tum verileri tek cagrida saglar.</summary>
[ApiController]
[Route("api/progress")]
[Authorize]
public class ProgressController : ControllerBase
{
    private readonly IProgressService _progressService;
    private readonly IActivityCalendarService _calendarService;
    private readonly IWeightReminderService _weightReminderService;

    public ProgressController(
        IProgressService progressService,
        IActivityCalendarService calendarService,
        IWeightReminderService weightReminderService)
    {
        _progressService = progressService;
        _calendarService = calendarService;
        _weightReminderService = weightReminderService;
    }

    /// <summary>Sorgu: /api/progress?range=3months ("week" | "month" | "3months" | "year", varsayilan "3months").</summary>
    [HttpGet]
    public async Task<ActionResult<CustomerProgressResponse>> GetProgress(
        [FromQuery] string? range,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _progressService.GetProgressAsync(userId, range ?? "3months", cancellationToken);
        return Ok(result);
    }

    /// <summary>Gunluk kilo girisi. Govde: { "weightKg": 82.4, "date": "2026-10-12" } (date istege bagli).</summary>
    [HttpPost("weight")]
    public async Task<ActionResult<CustomerWeightLogResponse>> LogWeight(
        [FromBody] LogWeightRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _progressService.LogWeightAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gunluk ozetler (antrenman, beslenme, harcanan kalori). Sorgu: /api/progress/calendar?from=2026-10-01&amp;to=2026-10-31
    /// (yerel tarihler, ikisi de dahil, en fazla 62 gun).
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<CustomerActivityCalendarResponse>> GetCalendar(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _calendarService.GetCalendarAsync(userId, from, to, cancellationToken));
    }

    /// <summary>Kilo guncelleme hatirlatmasinin simdi gosterilip gosterilmeyecegi (yaklasik iki gunde bir).</summary>
    [HttpGet("weight-reminder")]
    public async Task<ActionResult<CustomerWeightReminderResponse>> GetWeightReminder(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _weightReminderService.GetAsync(userId, cancellationToken));
    }

    /// <summary>Govde: { "action": "later" } (1 gun ertele) veya { "action": "dismiss" } (bir sonraki araliga kadar kapat).</summary>
    [HttpPost("weight-reminder/snooze")]
    public async Task<ActionResult<CustomerWeightReminderResponse>> SnoozeWeightReminder(
        [FromBody] SnoozeWeightReminderRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _weightReminderService.SnoozeAsync(userId, request, cancellationToken));
    }
}
