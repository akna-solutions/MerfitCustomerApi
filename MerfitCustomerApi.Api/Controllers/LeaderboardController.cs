using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Leaderboard;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>MerfitNativeApp LeaderboardScreen'inin ihtiyac duydugu tum verileri tek cagrida saglar.</summary>
[ApiController]
[Route("api/leaderboard")]
[Authorize]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;

    public LeaderboardController(ILeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    /// <summary>Sorgu: /api/leaderboard?period=month ("week" | "month" | "allTime", varsayilan "month").</summary>
    [HttpGet]
    public async Task<ActionResult<CustomerLeaderboardResponse>> GetLeaderboard(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _leaderboardService.GetLeaderboardAsync(userId, period ?? "month", cancellationToken);
        return Ok(result);
    }
}
