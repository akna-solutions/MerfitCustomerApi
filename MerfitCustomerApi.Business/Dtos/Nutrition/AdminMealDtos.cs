using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Nutrition;

/// <summary>GET /api/admin/meals liste satiri (madde 15 - salt okunur).</summary>
public class AdminMealListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalCalories { get; set; }
}

/// <summary>GET /api/admin/meals/{id} detay; ogun icerigindeki her bir besini de listeler.</summary>
public class AdminMealDetailDto : AdminMealListItemDto
{
    public List<AdminMealItemDetailDto> Items { get; set; } = new();
}

public class AdminMealItemDetailDto
{
    public long Id { get; set; }
    public long FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ServingSize { get; set; }
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
    public decimal? Fiber { get; set; }
}

public class AdminMealListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
