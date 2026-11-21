using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>Urun detayindaki "Benzer Urunler" ve "Senin Icin Onerilenler". Kullanici JWT'den okunur.</summary>
[ApiController]
[Route("api/store")]
[Authorize]
public class StoreRecommendationsController : ControllerBase
{
    private readonly IStoreRecommendationService _recommendations;

    public StoreRecommendationsController(IStoreRecommendationService recommendations)
    {
        _recommendations = recommendations;
    }

    [HttpGet("products/{productId:long}/recommendations")]
    public async Task<ActionResult<StoreProductRecommendationsDto>> Get(long productId, [FromQuery] int limit = StoreRecommendationService.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        Ok(await _recommendations.GetForProductAsync(User.GetUserId()!.Value, productId, limit, cancellationToken));
}
