using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Subscriptions;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>Mobil uygulamanin "Aboneliği Yönet" ekrani icin kullanicinin Plus uyeligi.</summary>
[ApiController]
[Route("api/subscription")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    /// <summary>Giris yapmis kullanicinin abonelik durumu ve satin alinabilir planlar.</summary>
    [HttpGet]
    public async Task<ActionResult<MySubscriptionResponse>> GetMine(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _subscriptionService.GetMySubscriptionAsync(userId, cancellationToken));
    }
}
