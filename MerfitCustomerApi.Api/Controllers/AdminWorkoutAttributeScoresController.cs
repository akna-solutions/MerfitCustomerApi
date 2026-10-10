using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Admin.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Antrenman ozellik puanlarinin (Hypertrophy, Strength, Cardio, Endurance, Mobility, CoreStability)
/// urun ekibi tarafindan girilmesi/guncellenmesi icin uc noktalar. Yalnizca Admin/SuperAdmin rolundeki
/// kullanicilar erisebilir ("AdminOnly" policy, bkz. Program.cs); mobil kullanicilar 403 alir.
/// </summary>
[ApiController]
[Route("api/admin/workouts/{workoutId:long}/attribute-scores")]
[Authorize(Policy = "AdminOnly")]
public class AdminWorkoutAttributeScoresController : ControllerBase
{
    private readonly IWorkoutAttributeScoreService _service;

    public AdminWorkoutAttributeScoresController(IWorkoutAttributeScoreService service)
    {
        _service = service;
    }

    /// <summary>Antrenmanin mevcut ozellik puanlarini ve henuz puanlanmamis ozellik turlerini dondurur.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminWorkoutAttributeScoresDto>> Get(long workoutId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(workoutId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Antrenmanin ozellik puanlarini tamamen degistirir (replace-all).</summary>
    [HttpPut]
    public async Task<ActionResult<AdminWorkoutAttributeScoresDto>> Set(
        long workoutId,
        [FromBody] AdminSetWorkoutAttributeScoresRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetAsync(workoutId, request, User.GetUserId(), cancellationToken);
        return Ok(result);
    }
}
