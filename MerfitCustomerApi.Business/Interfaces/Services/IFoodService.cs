using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>AddMealModal'daki yiyecek arama listesini besleyen servis sozlesmesi.</summary>
public interface IFoodService
{
    Task<PagedResult<CustomerFoodListItemDto>> SearchFoodsAsync(CustomerFoodListRequest request, CancellationToken cancellationToken = default);
}
