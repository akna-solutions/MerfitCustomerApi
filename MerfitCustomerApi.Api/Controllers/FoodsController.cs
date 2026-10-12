using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>MerfitNativeApp AddMealModal'daki yiyecek arama uc noktasi.</summary>
[ApiController]
[Route("api/foods")]
[Authorize]
public class FoodsController : ControllerBase
{
    private readonly IFoodService _foodService;

    public FoodsController(IFoodService foodService)
    {
        _foodService = foodService;
    }

    /// <summary>Isme gore yiyecek arar. Sorgu: /api/foods?search=tavuk&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerFoodListItemDto>>> SearchFoods(
        [FromQuery] CustomerFoodListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _foodService.SearchFoodsAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Barkodla besin getirir (katalogda yoksa Open Food Facts'ten). Degerler ServingSize/ServingUnit
    /// basinadir; barkodla eklenen urunlerde bu 100 g veya 100 ml'dir. Sorgu: /api/foods/barcode/8690504012011
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<CustomerFoodListItemDto>> GetByBarcode(string barcode, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _foodService.GetByBarcodeAsync(userId, barcode, cancellationToken);
        return Ok(result);
    }
}
