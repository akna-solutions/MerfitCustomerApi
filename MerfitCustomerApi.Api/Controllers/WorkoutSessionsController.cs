using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp'in aktif antrenman (ActiveWorkout) akisini destekleyen uc noktalar: baslat,
/// set logla, tamamla, iptal et. Tum uc noktalar sadece cagiran kullaniciya ait oturumlara izin verir.
/// </summary>
[ApiController]
[Route("api/workout-sessions")]
[Authorize]
public class WorkoutSessionsController : ControllerBase
{
    private readonly IWorkoutSessionService _workoutSessionService;

    public WorkoutSessionsController(IWorkoutSessionService workoutSessionService)
    {
        _workoutSessionService = workoutSessionService;
    }

    /// <summary>Secilen antrenman icin yeni bir oturum baslatir ve egzersiz/set sablonunu dondurur.</summary>
    [HttpPost]
    public async Task<ActionResult<CustomerWorkoutSessionDto>> StartSession(
        [FromBody] StartWorkoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _workoutSessionService.StartSessionAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Devam eden veya gecmis bir oturumun guncel durumunu dondurur.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<CustomerWorkoutSessionDto>> GetSession(long id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _workoutSessionService.GetSessionAsync(userId, id, cancellationToken);
        return Ok(result);
    }

    /// <summary>Bir egzersizdeki tek bir seti loglar (ayni set numarasi tekrar gonderilirse guncellenir).</summary>
    [HttpPost("{id:long}/exercises/{sessionExerciseId:long}/sets")]
    public async Task<ActionResult<CustomerWorkoutSessionExerciseDto>> LogSet(
        long id,
        long sessionExerciseId,
        [FromBody] LogWorkoutSetRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _workoutSessionService.LogSetAsync(userId, id, sessionExerciseId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Oturumu tamamlar; seri guncellemesi ve yeni kisisel rekor tespiti bu adimda yapilir.</summary>
    [HttpPut("{id:long}/complete")]
    public async Task<ActionResult<CustomerWorkoutSessionSummaryDto>> CompleteSession(
        long id,
        [FromBody] CompleteWorkoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _workoutSessionService.CompleteSessionAsync(userId, id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Oturumu yarim birakildi (iptal) olarak isaretler.</summary>
    [HttpPut("{id:long}/cancel")]
    public async Task<IActionResult> CancelSession(long id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _workoutSessionService.CancelSessionAsync(userId, id, cancellationToken);
        return NoContent();
    }
}
