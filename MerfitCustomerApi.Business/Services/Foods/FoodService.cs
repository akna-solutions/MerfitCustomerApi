using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;

namespace MerfitCustomerApi.Business.Services.Foods;

/// <summary>IFoodService'in varsayilan implementasyonu. Mevcut Food entity'sini oldugu gibi kullanir.</summary>
public class FoodService : IFoodService
{
    private readonly IUnitOfWork _unitOfWork;

    public FoodService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CustomerFoodListItemDto>> SearchFoodsAsync(CustomerFoodListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Food>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(f => f.Name.ToLower().Contains(term));
        }

        query = query.OrderBy(f => f.Name);

        var paged = await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        var items = paged.Items.Select(f => new CustomerFoodListItemDto
        {
            Id = f.Id,
            Name = f.Name,
            Brand = f.Brand,
            ServingSize = f.ServingSize,
            ServingUnit = f.ServingUnit,
            Calories = (int)Math.Round(f.Calories),
            Protein = f.Protein,
            Carbs = f.Carbs,
            Fat = f.Fat,
            ImageUrl = f.ImageUrl,
        }).ToList();

        return PagedResult<CustomerFoodListItemDto>.Create(items, paged.Page, paged.PageSize, paged.TotalCount);
    }
}
