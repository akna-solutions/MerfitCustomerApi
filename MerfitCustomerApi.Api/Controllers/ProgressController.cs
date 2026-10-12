using MerfitCustomerApi.Api.Extensions;
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

    public ProgressController(IProgressService progressService)
    {
        _progressService = progressService;
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
}
