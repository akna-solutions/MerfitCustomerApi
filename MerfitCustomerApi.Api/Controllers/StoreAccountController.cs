using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>MB Shop favorileri ve teslimat adresleri. Kayitlar yalnizca JWT'deki kullaniciya aittir.</summary>
[ApiController]
[Route("api/store")]
[Authorize]
public class StoreAccountController : ControllerBase
{
    private readonly IStoreFavoriteService _favorites;
    private readonly IStoreAddressService _addresses;

    public StoreAccountController(IStoreFavoriteService favorites, IStoreAddressService addresses)
    {
        _favorites = favorites;
        _addresses = addresses;
    }

    private long UserId => User.GetUserId()!.Value;

    [HttpGet("favorites")]
    public async Task<ActionResult<List<StoreProductListItemDto>>> GetFavorites(CancellationToken cancellationToken)
        => Ok(await _favorites.GetAsync(UserId, cancellationToken));

    /// <summary>Idempotent ekleme.</summary>
    [HttpPut("favorites/{productId:long}")]
    public async Task<IActionResult> AddFavorite(long productId, CancellationToken cancellationToken)
    {
        await _favorites.AddAsync(UserId, productId, cancellationToken);
        return NoContent();
    }

    /// <summary>Idempotent cikarma.</summary>
    [HttpDelete("favorites/{productId:long}")]
    public async Task<IActionResult> RemoveFavorite(long productId, CancellationToken cancellationToken)
    {
        await _favorites.RemoveAsync(UserId, productId, cancellationToken);
        return NoContent();
    }

    [HttpGet("addresses")]
    public async Task<ActionResult<List<StoreAddressDto>>> GetAddresses(CancellationToken cancellationToken)
        => Ok(await _addresses.GetAsync(UserId, cancellationToken));

    [HttpPost("addresses")]
    public async Task<ActionResult<StoreAddressDto>> CreateAddress([FromBody] UpsertStoreAddressRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await _addresses.CreateAsync(UserId, request, cancellationToken));

    [HttpPut("addresses/{id:long}")]
    public async Task<ActionResult<StoreAddressDto>> UpdateAddress(long id, [FromBody] UpsertStoreAddressRequest request, CancellationToken cancellationToken)
        => Ok(await _addresses.UpdateAsync(UserId, id, request, cancellationToken));

    [HttpDelete("addresses/{id:long}")]
    public async Task<IActionResult> DeleteAddress(long id, CancellationToken cancellationToken)
    {
        await _addresses.DeleteAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("addresses/{id:long}/default")]
    public async Task<ActionResult<StoreAddressDto>> SetDefaultAddress(long id, CancellationToken cancellationToken)
        => Ok(await _addresses.SetDefaultAsync(UserId, id, cancellationToken));
}
