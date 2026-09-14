using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutPlans;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp "Kişisel Programım" ekraninin kullandigi, kullaniciya ozel (Faz 2'de
/// PersonalizationJobProcessor tarafindan onceden uretilip kaydedilmis) antrenman programi
/// uc noktalari. Genel antrenman kataloğu icin bkz. WorkoutsController (/api/workouts) -
/// bu iki uc nokta seti birbirinden bagimsizdir ve birbirini etkilemez.
/// UserId her zaman JWT claim'inden okunur; istekte gonderilen bir kullanici kimligine
/// guvenilmez (IDOR korumasi).
/// </summary>
[ApiController]
[Route("api/my-plan")]
[Authorize]
public class MyPlanController : ControllerBase
{
    private readonly IMyPlanService _myPlanService;

    public MyPlanController(IMyPlanService myPlanService)
    {
        _myPlanService = myPlanService;
    }

    /// <summary>
    /// Giris yapmis kullanicinin aktif kisisel antrenman programini dondurur. Henuz bir plan
    /// olusturulmadiysa (PersonalizationJob hala Pending/Processing/Failed durumundaysa) 200 OK
    /// ile null govde doner - mobil taraf bunu personalization/status ile birlikte yorumlamalidir.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CustomerWorkoutPlanDto?>> GetMyPlan(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _myPlanService.GetActivePlanAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Bugunun (sunucu/UTC gunu) aktif plandaki karsiligini dondurur. Aktif plan yoksa veya
    /// bugun icin bir antrenman atanmamissa 404 DEGIL, HasWorkoutToday=false ile 200 OK doner.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<CustomerTodayWorkoutPlanResponse>> GetTodayPlan(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _myPlanService.GetTodayAsync(userId, cancellationToken);
        return Ok(result);
    }
}
