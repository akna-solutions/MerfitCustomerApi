using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Dashboard;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp DashboardScreen'inin ihtiyac duydugu tum verileri tek cagrida saglar.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>Giris yapmis kullanicinin dashboard verisini dondurur.</summary>
    [HttpGet]
    public async Task<ActionResult<CustomerDashboardResponse>> GetDashboard(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _dashboardService.GetDashboardAsync(userId, cancellationToken);
        return Ok(result);
    }
}
