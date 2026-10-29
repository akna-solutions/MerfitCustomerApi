using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MB Shop odeme secenekleri ve siparisler. Siparis tutari ve stok sunucuda hesaplanir; ayni IdempotencyKey ile
/// tekrarlanan istek ikinci siparis olusturmaz (ilk siparis 200 ile doner). Kullanici yalnizca kendi siparislerini gorur.
/// </summary>
[ApiController]
[Route("api/store")]
[Authorize]
public class StoreOrdersController : ControllerBase
{
    private readonly IStoreOrderService _orders;

    public StoreOrdersController(IStoreOrderService orders)
    {
        _orders = orders;
    }

    private long UserId => User.GetUserId()!.Value;

    [HttpGet("checkout/options")]
    public async Task<ActionResult<StoreCheckoutOptionsDto>> GetCheckoutOptions(CancellationToken cancellationToken)
        => Ok(await _orders.GetCheckoutOptionsAsync(cancellationToken));

    /// <summary>201: yeni siparis; 200: ayni anahtarla daha once olusturulmus siparis; 409: sepet/fiyat/stok degisti.</summary>
    [HttpPost("orders")]
    public async Task<ActionResult<StoreOrderDto>> Create([FromBody] CreateStoreOrderRequest request, CancellationToken cancellationToken)
    {
        var (order, created) = await _orders.CreateAsync(UserId, request, cancellationToken);
        return created ? StatusCode(StatusCodes.Status201Created, order) : Ok(order);
    }

    [HttpGet("orders")]
    public async Task<ActionResult<PagedResult<StoreOrderListItemDto>>> GetList([FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => Ok(await _orders.GetListAsync(UserId, request, cancellationToken));

    [HttpGet("orders/{id:long}")]
    public async Task<ActionResult<StoreOrderDto>> Get(long id, CancellationToken cancellationToken)
        => Ok(await _orders.GetAsync(UserId, id, cancellationToken));

    [HttpPost("orders/{id:long}/cancel")]
    public async Task<ActionResult<StoreOrderDto>> Cancel(long id, [FromBody] CancelStoreOrderRequest? request, CancellationToken cancellationToken)
        => Ok(await _orders.CancelAsync(UserId, id, request ?? new CancelStoreOrderRequest(), cancellationToken));
}
