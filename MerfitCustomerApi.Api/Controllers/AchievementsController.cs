using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Achievements;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>Kullanicinin basarilari (rozetler) ve yeni kazanim kutlamasinin bir kez gosterilmesi.</summary>
[ApiController]
[Route("api/achievements")]
[Authorize]
public class AchievementsController : ControllerBase
{
    private readonly IAchievementService _achievementService;

    public AchievementsController(IAchievementService achievementService)
    {
        _achievementService = achievementService;
    }

    [HttpGet]
    public async Task<ActionResult<CustomerAchievementsResponse>> GetMine(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _achievementService.GetMyAchievementsAsync(userId, cancellationToken));
    }

    /// <summary>Govde: { "achievementIds": [3, 5] }. Kutlamasi gosterilen kazanimlari isaretler.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> MarkSeen([FromBody] MarkAchievementsSeenRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _achievementService.MarkSeenAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
