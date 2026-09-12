using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Customer.Foods;

/// <summary>AddMealModal'daki yiyecek arama listesi icin. Mobil taraftaki FoodItem'a Calories alani ile eslesir (porsiyon basina).</summary>
public class CustomerFoodListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingUnit { get; set; } = string.Empty;
    public int Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
    public string? ImageUrl { get; set; }
}

public class CustomerFoodListRequest : PagedRequest
{
}
