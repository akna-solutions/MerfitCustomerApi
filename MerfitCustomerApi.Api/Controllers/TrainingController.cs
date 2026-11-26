using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Training;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Kisisellestirilmis antrenman motoru: tercih, program uretimi, versiyonlar, guncelleme onerisi
/// (onaysiz aktif program degismez), egzersiz alternatifi/degisimi, gelisim analizi, toparlanma ve AI kocu.
/// Egzersizler yalnizca mevcut katalogdan secilir.
/// </summary>
[ApiController]
[Route("api/training")]
[Authorize]
public class TrainingController : ControllerBase
{
    private readonly ITrainingProgramService _programs;
    private readonly ITrainingCoachService _coach;

    public TrainingController(ITrainingProgramService programs, ITrainingCoachService coach)
    {
        _programs = programs;
        _coach = coach;
    }

    private long UserId => User.GetUserId()!.Value;

    [HttpGet("preferences")]
    public async Task<ActionResult<TrainingPreferencesDto>> GetPreferences(CancellationToken ct)
        => Ok(await _programs.GetPreferencesAsync(UserId, ct));

    [HttpPut("preferences")]
    public async Task<ActionResult<TrainingPreferencesDto>> UpdatePreferences([FromBody] TrainingPreferencesDto request, CancellationToken ct)
        => Ok(await _programs.UpdatePreferencesAsync(UserId, request, ct));

    /// <summary>Yeni program uretir. Aktif bir motor programi varsa yeni versiyon "onerilen" olarak kaydedilir.</summary>
    [HttpPost("programs/generate")]
    public async Task<ActionResult<TrainingProgramDto>> Generate([FromBody] GenerateTrainingProgramRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _programs.GenerateAsync(UserId, request, ct));

    [HttpGet("programs/active")]
    public async Task<ActionResult<TrainingProgramDto>> GetActive(CancellationToken ct)
    {
        var result = await _programs.GetActiveAsync(UserId, ct);
        return result is null ? NoContent() : Ok(result);
    }

    /// <summary>
    /// Versiyonlar (aktif, onerilen, arsivlenmis, reddedilmis), en yeniden eskiye. Istege bagli sayfalama:
    /// ?offset=0&amp;limit=20 (limit en fazla 100). Parametre verilmezse tum liste doner (eski davranis).
    /// </summary>
    [HttpGet("programs")]
    public async Task<ActionResult<IReadOnlyList<TrainingProgramSummaryDto>>> GetVersions([FromQuery] int? offset, [FromQuery] int? limit, CancellationToken ct)
        => Ok(await _programs.GetVersionsAsync(UserId, offset, limit, ct));

    [HttpGet("programs/{id:long}")]
    public async Task<ActionResult<TrainingProgramDto>> GetProgram(long id, CancellationToken ct)
        => Ok(await _programs.GetProgramAsync(UserId, id, ct));

    [HttpGet("update-recommendation")]
    public async Task<ActionResult<TrainingUpdateRecommendationDto>> GetUpdateRecommendation(CancellationToken ct)
        => Ok(await _programs.GetUpdateRecommendationAsync(UserId, ct));

    [HttpPost("proposals")]
    public async Task<ActionResult<TrainingProgramDto>> Propose(CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _programs.ProposeUpdateAsync(UserId, ct));

    [HttpPost("programs/{id:long}/accept")]
    public async Task<ActionResult<TrainingProgramDto>> Accept(long id, CancellationToken ct)
        => Ok(await _programs.AcceptProposalAsync(UserId, id, ct));

    [HttpPost("programs/{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, CancellationToken ct)
    {
        await _programs.RejectProposalAsync(UserId, id, ct);
        return NoContent();
    }

    [HttpGet("exercises/{planExerciseId:long}/alternatives")]
    public async Task<ActionResult<IReadOnlyList<TrainingAlternativeDto>>> GetAlternatives(long planExerciseId, CancellationToken ct)
        => Ok(await _programs.GetAlternativesAsync(UserId, planExerciseId, ct));

    /// <summary>Kullanicinin elle yaptigi degisim yeni bir versiyon olusturur; eski versiyon ve gecmis korunur.</summary>
    [HttpPost("exercises/{planExerciseId:long}/swap")]
    public async Task<ActionResult<TrainingProgramDto>> Swap(long planExerciseId, [FromBody] SwapExerciseRequest request, CancellationToken ct)
        => Ok(await _programs.SwapExerciseAsync(UserId, planExerciseId, request, ct));

    [HttpGet("progress")]
    public async Task<ActionResult<TrainingProgressDto>> GetProgress(CancellationToken ct)
        => Ok(await _programs.GetProgressAsync(UserId, ct));

    [HttpGet("exercises/{exerciseId:long}/history")]
    public async Task<ActionResult<TrainingExerciseProgressDto>> GetExerciseHistory(long exerciseId, CancellationToken ct)
    {
        var result = await _programs.GetExerciseHistoryAsync(UserId, exerciseId, ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpGet("recovery")]
    public async Task<ActionResult<TrainingRecoveryDto>> GetRecovery(CancellationToken ct)
        => Ok(await _programs.GetRecoveryAsync(UserId, ct));

    /// <summary>AI yalnizca aciklama yapar; yanit backend tarafinda dogrulanir, gecmezse kural tabanli metin doner.</summary>
    [HttpPost("coach")]
    public async Task<ActionResult<TrainingCoachResponse>> Coach([FromBody] TrainingCoachRequest request, CancellationToken ct)
        => Ok(await _coach.AskAsync(UserId, request, ct));
}
