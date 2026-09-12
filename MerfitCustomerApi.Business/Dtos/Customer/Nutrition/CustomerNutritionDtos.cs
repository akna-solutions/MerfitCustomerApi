using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Nutrition;

/// <summary>
/// MerfitNativeApp NutritionScreen'in ihtiyac duydugu tum verileri tek cagrida saglayan response.
/// Sekil, mobil taraftaki NutritionData tipi ile birebir eslesecek sekilde tasarlandi.
/// </summary>
public class CustomerNutritionResponse
{
    public bool HasLoggedFirstMeal { get; set; }

    /// <summary>Gunluk hedeflenen kalori (NutritionGoal'dan; yoksa profile gore hesaplanip olusturulur).</summary>
    public int DailyCalories { get; set; }

    public CustomerMacroOverviewDto Macros { get; set; } = new();

    public CustomerWaterDto Water { get; set; } = new();

    public List<CustomerMealEntryDto> Meals { get; set; } = new();
}

public class CustomerMacroOverviewDto
{
    public CustomerMacroTargetDto Protein { get; set; } = new();
    public CustomerMacroTargetDto Carbs { get; set; } = new();
    public CustomerMacroTargetDto Fats { get; set; } = new();
}

public class CustomerMacroTargetDto
{
    public int Consumed { get; set; }
    public int Target { get; set; }
}

public class CustomerWaterDto
{
    public decimal ConsumedL { get; set; }
    public decimal TargetL { get; set; }
}

/// <summary>
/// Bir gunde loglanan tek bir besin kaydi (MealItem). Mobil taraftaki MealEntry ile birebir
/// eslesir; "Type" o besinin bagli oldugu ogunu (Kahvalti/Ogle/Aksam/Atistirmalik) tasir.
/// </summary>
public class CustomerMealEntryDto
{
    /// <summary>MealItem.Id - silme/duzenleme icin kullanilir.</summary>
    public long Id { get; set; }

    /// <summary>"Kahvaltı" | "Öğle Yemeği" | "Akşam Yemeği" | "Atıştırmalık".</summary>
    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int Calories { get; set; }
}

/// <summary>POST /api/nutrition/meals govdesi.</summary>
public class LogMealItemRequest
{
    /// <summary>"yyyy-MM-dd"; verilmezse bugun kabul edilir.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>"Breakfast" | "Lunch" | "Dinner" | "Snack" (Domain.Enums.MealType).</summary>
    [Required(ErrorMessage = "MealType alani zorunludur.")]
    public string MealType { get; set; } = string.Empty;

    [Required(ErrorMessage = "FoodId alani zorunludur.")]
    public long FoodId { get; set; }

    /// <summary>Food.ServingSize'a gore carpan (orn. 1 = tek porsiyon, 1.5 = bir bucuk porsiyon).</summary>
    [Range(0.01, 100, ErrorMessage = "Quantity gecerli bir deger olmalidir.")]
    public decimal Quantity { get; set; } = 1;
}

/// <summary>POST /api/nutrition/water govdesi.</summary>
public class LogWaterRequest
{
    [Range(1, 5000, ErrorMessage = "AmountMl gecerli bir deger olmalidir.")]
    public decimal AmountMl { get; set; }

    /// <summary>"yyyy-MM-dd"; verilmezse bugun kabul edilir.</summary>
    public DateOnly? Date { get; set; }
}
