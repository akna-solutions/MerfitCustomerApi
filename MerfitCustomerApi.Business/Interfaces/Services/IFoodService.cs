using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>AddMealModal'daki yiyecek arama listesini besleyen servis sozlesmesi.</summary>
public interface IFoodService
{
    Task<PagedResult<CustomerFoodListItemDto>> SearchFoodsAsync(CustomerFoodListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Barkoda ait besini getirir. Katalogda yoksa Open Food Facts'ten 100 g/ml basina degerleriyle
    /// bulup kataloga ekler. Bulunamazsa NotFoundException, servise ulasilamazsa
    /// ExternalServiceUnavailableException firlatir.
    /// </summary>
    Task<CustomerFoodListItemDto> GetByBarcodeAsync(long userId, string barcode, CancellationToken cancellationToken = default);
}
