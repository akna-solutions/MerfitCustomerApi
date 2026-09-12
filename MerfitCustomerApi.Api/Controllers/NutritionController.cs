using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp NutritionScreen'inin gunluk beslenme ozeti, ogun ve su loglama uc noktalari.
/// </summary>
[ApiController]
[Route("api/nutrition")]
[Authorize]
public class NutritionController : ControllerBase
{
    private readonly INutritionService _nutritionService;

    public NutritionController(INutritionService nutritionService)
    {
        _nutritionService = nutritionService;
    }

    /// <summary>Verilen gune (varsayilan: bugun) ait beslenme ozetini dondurur. Sorgu: /api/nutrition?date=2026-09-12</summary>
    [HttpGet]
    public async Task<ActionResult<CustomerNutritionResponse>> GetDailyNutrition(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _nutritionService.GetDailyNutritionAsync(userId, targetDate, cancellationToken);
        return Ok(result);
    }

    /// <summary>Secilen besini gunluge (Add Meal akisi) ekler.</summary>
    [HttpPost("meals")]
    public async Task<ActionResult<CustomerMealEntryDto>> LogMealItem(
        [FromBody] LogMealItemRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _nutritionService.LogMealItemAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Su tuketimi ekler ve guncel gunluk toplami dondurur.</summary>
    [HttpPost("water")]
    public async Task<ActionResult<CustomerWaterDto>> LogWater(
        [FromBody] LogWaterRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _nutritionService.LogWaterAsync(userId, request, cancellationToken);
        return Ok(result);
    }
}
