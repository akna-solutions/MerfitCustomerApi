using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Kullanicinin kalici sepeti. Kullanici yalnizca JWT'den okunur; baska kullanicinin satiri 404 doner. Her yanit
/// guncel fiyatlarla sunucuda hesaplanmis sepeti ve toplamlari icerir; istemciden fiyat alinmaz.
/// </summary>
[ApiController]
[Route("api/store/cart")]
[Authorize]
public class StoreCartController : ControllerBase
{
    private readonly IStoreCartService _cart;

    public StoreCartController(IStoreCartService cart)
    {
        _cart = cart;
    }

    private long UserId => User.GetUserId()!.Value;

    [HttpGet]
    public async Task<ActionResult<StoreCartDto>> Get(CancellationToken cancellationToken)
        => Ok(await _cart.GetAsync(UserId, cancellationToken));

    [HttpPost("items")]
    public async Task<ActionResult<StoreCartDto>> AddItem([FromBody] AddCartItemRequest request, CancellationToken cancellationToken)
        => Ok(await _cart.AddItemAsync(UserId, request, cancellationToken));

    [HttpPatch("items/{itemId:long}")]
    public async Task<ActionResult<StoreCartDto>> UpdateItem(long itemId, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken)
        => Ok(await _cart.UpdateItemAsync(UserId, itemId, request, cancellationToken));

    [HttpDelete("items/{itemId:long}")]
    public async Task<ActionResult<StoreCartDto>> RemoveItem(long itemId, CancellationToken cancellationToken)
        => Ok(await _cart.RemoveItemAsync(UserId, itemId, cancellationToken));

    [HttpDelete]
    public async Task<ActionResult<StoreCartDto>> Clear(CancellationToken cancellationToken)
        => Ok(await _cart.ClearAsync(UserId, cancellationToken));
}
