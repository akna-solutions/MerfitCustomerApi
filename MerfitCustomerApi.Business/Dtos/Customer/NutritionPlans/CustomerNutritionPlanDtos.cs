namespace MerfitCustomerApi.Business.Dtos.Customer.NutritionPlans;

/// <summary>
/// GET /api/my-nutrition-plan yaniti. Kullanicinin aktif kisisel beslenme programinin tam
/// gorunumu. MerfitNativeApp "Bugünkü Beslenme Planım" ekraninin ihtiyac duydugu veriyi saglar.
/// </summary>
public class CustomerNutritionPlanDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Kurallardan (AI kullanilmadan) uretilmis, kullaniciya gosterilebilir kisa aciklama.</summary>
    public string? Summary { get; set; }

    public List<CustomerNutritionPlanDayDto> Days { get; set; } = new();
}

public class CustomerNutritionPlanDayDto
{
    /// <summary>"Monday" | "Tuesday" | ... (System.DayOfWeek.ToString()).</summary>
    public string DayOfWeek { get; set; } = string.Empty;

    public List<CustomerNutritionPlanMealDto> Meals { get; set; } = new();
}

public class CustomerNutritionPlanMealDto
{
    /// <summary>"Breakfast" | "Lunch" | "Dinner" | "Snack".</summary>
    public string MealType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>"HH:mm" formatinda onerilen saat; belirtilmemisse null.</summary>
    public string? PlannedTime { get; set; }

    public string? Notes { get; set; }

    public List<CustomerNutritionPlanMealItemDto> Items { get; set; } = new();

    public int TotalCalories { get; set; }
    public decimal TotalProtein { get; set; }
    public decimal TotalCarbs { get; set; }
    public decimal TotalFat { get; set; }
}

public class CustomerNutritionPlanMealItemDto
{
    public long FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string ServingUnit { get; set; } = string.Empty;
    public int Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}

/// <summary>
/// GET /api/my-nutrition-plan/today yaniti. Bugune (System.DayOfWeek) ait beslenme planini
/// dondurur. Aktif plan yoksa 404 firlatilmaz; HasPlanToday=false doner.
/// </summary>
public class CustomerTodayNutritionPlanResponse
{
    public bool HasPlanToday { get; set; }
    public bool HasActivePlan { get; set; }
    public CustomerNutritionPlanDayDto? Day { get; set; }
}
