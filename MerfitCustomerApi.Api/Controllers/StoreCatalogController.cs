using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MB Shop katalogu (kategoriler, kampanyalar, urunler). Urunler admin panelinden (MerfitApi) yonetilir; burada yalnizca
/// aktif kayitlar okunur. Filtre, siralama ve sayfalama sunucuda yapilir.
/// </summary>
[ApiController]
[Route("api/store")]
[Authorize]
public class StoreCatalogController : ControllerBase
{
    private readonly IStoreCatalogService _catalog;

    public StoreCatalogController(IStoreCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<StoreCategoryDto>>> GetCategories(CancellationToken cancellationToken)
        => Ok(await _catalog.GetCategoriesAsync(cancellationToken));

    [HttpGet("campaigns")]
    public async Task<ActionResult<List<StoreCampaignDto>>> GetCampaigns(CancellationToken cancellationToken)
        => Ok(await _catalog.GetCampaignsAsync(cancellationToken));

    [HttpGet("brands")]
    public async Task<ActionResult<List<string>>> GetBrands([FromQuery] long? categoryId, CancellationToken cancellationToken)
        => Ok(await _catalog.GetBrandsAsync(categoryId, cancellationToken));

    /// <summary>Sayfali urun listesi: q, categoryId, campaignId, collection, brands, minPrice, maxPrice, inStockOnly, discountedOnly, sort.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<PagedResult<StoreProductListItemDto>>> GetProducts([FromQuery] StoreProductQuery query, CancellationToken cancellationToken)
        => Ok(await _catalog.GetProductsAsync(query, cancellationToken));

    [HttpGet("products/{id:long}")]
    public async Task<ActionResult<StoreProductDetailDto>> GetProduct(long id, CancellationToken cancellationToken)
        => Ok(await _catalog.GetProductAsync(id, cancellationToken));
}
