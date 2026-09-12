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
}
