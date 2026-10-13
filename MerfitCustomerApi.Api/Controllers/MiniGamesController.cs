using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp'teki Dumbbell Dash mini oyununun (dinlenme ekrani ve MB FIT Puanim ekrani) oturum ve
/// odul uc noktalari. Kullanici kimligi yalnizca JWT'den okunur; skor sunucuda yeniden hesaplanir.
/// </summary>
[ApiController]
[Route("api/mini-games/dumbbell-dash/sessions")]
[Authorize]
public class MiniGamesController : ControllerBase
{
    private readonly IMiniGameService _miniGameService;

    public MiniGamesController(IMiniGameService miniGameService)
    {
        _miniGameService = miniGameService;
    }

    /// <summary>Yeni oyun oturumu acar: seed, odul tablosu ve bugunku kalan puan doner. Gunluk oturum siniri asilirsa 429.</summary>
    [HttpPost]
    public async Task<ActionResult<MiniGameSessionDto>> StartSession(
        [FromBody] StartMiniGameSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _miniGameService.StartSessionAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Oyun sonucunu dogrular ve odulu verir. Idempotent: ayni oturum tekrar gonderilirse ilk sonuc doner.</summary>
    [HttpPost("{id:long}/finish")]
    public async Task<ActionResult<MiniGameResultDto>> FinishSession(
        long id,
        [FromBody] FinishMiniGameSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _miniGameService.FinishSessionAsync(userId, id, request, cancellationToken);
        return Ok(result);
    }
}
