using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp Workouts ekraninin antrenman katalogu (listeleme/filtreleme/detay) uc noktalari.
/// </summary>
[ApiController]
[Route("api/workouts")]
[Authorize]
public class WorkoutsController : ControllerBase
{
    private readonly IWorkoutService _workoutService;

    public WorkoutsController(IWorkoutService workoutService)
    {
        _workoutService = workoutService;
    }

    /// <summary>
    /// Filtrelenmis, sayfalanmis antrenman listesini dondurur.
    /// Sorgu ornegi: /api/workouts?difficulty=Beginner&amp;duration=under20,20to40&amp;equipment=dumbbells&amp;page=1&amp;pageSize=20
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerWorkoutListItemDto>>> GetWorkouts(
        [FromQuery] CustomerWorkoutListRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _workoutService.GetWorkoutsAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Tek bir antrenmanin, egzersiz listesiyle birlikte detayini dondurur (workout/ActiveWorkout onizlemesi).</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<CustomerWorkoutDetailDto>> GetWorkoutDetail(long id, CancellationToken cancellationToken)
    {
        var result = await _workoutService.GetWorkoutDetailAsync(id, cancellationToken);
        return Ok(result);
    }
}
