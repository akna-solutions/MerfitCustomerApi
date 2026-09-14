using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionPlans;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp "Bugünkü Beslenme Planım" ekraninin kullandigi, kullaniciya ozel (Faz 2'de
/// NutritionPlanGenerator tarafindan onceden uretilip kaydedilmis) beslenme programi uc noktalari.
/// Gercekte tuketilen ogunlerin loglandigi /api/nutrition (NutritionController, Meal/MealItem)
/// ile karistirilmamalidir - bu iki uc nokta seti kavramsal olarak farklidir (PLAN vs LOG).
/// UserId JWT claim'inden okunur.
/// </summary>
[ApiController]
[Route("api/my-nutrition-plan")]
[Authorize]
public class MyNutritionController : ControllerBase
{
    private readonly IMyNutritionPlanService _myNutritionPlanService;

    public MyNutritionController(IMyNutritionPlanService myNutritionPlanService)
    {
        _myNutritionPlanService = myNutritionPlanService;
    }

    /// <summary>Giris yapmis kullanicinin aktif kisisel beslenme programini dondurur; yoksa null.</summary>
    [HttpGet]
    public async Task<ActionResult<CustomerNutritionPlanDto?>> GetMyNutritionPlan(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _myNutritionPlanService.GetActivePlanAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Bugunun aktif plandaki beslenme gunune karsiligini dondurur; yoksa HasPlanToday=false.</summary>
    [HttpGet("today")]
    public async Task<ActionResult<CustomerTodayNutritionPlanResponse>> GetTodayNutritionPlan(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _myNutritionPlanService.GetTodayAsync(userId, cancellationToken);
        return Ok(result);
    }
}
