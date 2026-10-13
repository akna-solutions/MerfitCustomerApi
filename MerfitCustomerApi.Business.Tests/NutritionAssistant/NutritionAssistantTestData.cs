using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Services.NutritionAssistant;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

internal static class NutritionAssistantTestData
{
    /// <summary>Hedefler: 2100 kcal, 130 g protein, 230 g karbonhidrat, 70 g yag.</summary>
    public static CustomerNutritionResponse Day(int kcal, int protein, int carbs, int fats, params (string Type, string Name, int Kcal)[] meals)
    {
        var entries = meals.Length > 0
            ? meals.Select((m, i) => new CustomerMealEntryDto { Id = i + 1, Type = m.Type, Name = m.Name, Calories = m.Kcal }).ToList()
            : kcal > 0 ? new List<CustomerMealEntryDto> { new() { Id = 1, Type = "Kahvaltı", Name = "Yulaf", Calories = kcal } } : new();
        var day = new CustomerNutritionResponse
        {
            HasLoggedFirstMeal = entries.Count > 0,
            DailyCalories = 2100,
            Macros = new CustomerMacroOverviewDto
            {
                Protein = new() { Consumed = protein, Target = 130 },
                Carbs = new() { Consumed = carbs, Target = 230 },
                Fats = new() { Consumed = fats, Target = 70 },
            },
            Water = new CustomerWaterDto { ConsumedL = 1, TargetL = 2.5m },
            Meals = entries,
        };
        day.Insight = NutritionInsightEvaluator.Evaluate(day);
        return day;
    }

    /// <summary>Gereksinimdeki ornek senaryo: protein 65 g / 130 g, karbonhidrat 180 g, yag 55 g.</summary>
    public static CustomerNutritionResponse SpecExample() =>
        Day(1460, 65, 180, 55, ("Kahvaltı", "Yulaf", 420), ("Öğle Yemeği", "Makarna", 740), ("Atıştırmalık", "Muz", 300));
}
