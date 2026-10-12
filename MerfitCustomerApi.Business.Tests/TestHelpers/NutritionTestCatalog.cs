using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Tests.TestHelpers;

/// <summary>
/// Beslenme plani testleri icin, yaygin besin tablosu degerlerine yakin, admin tarafindan
/// siniflandirilmis gibi kategori/ogun/alerjen/diyet bilgisi girilmis kucuk bir katalog.
/// Ayrica siniflandirilmamis ve enerji degeri tutarsiz kayitlar da icerir; bunlar plana girmemelidir.
/// </summary>
internal static class NutritionTestCatalog
{
    private const MealSuitability B = MealSuitability.Breakfast;
    private const MealSuitability L = MealSuitability.Lunch;
    private const MealSuitability D = MealSuitability.Dinner;
    private const MealSuitability S = MealSuitability.Snack;

    public const long UnclassifiedFoodId = 90;
    public const long InconsistentEnergyFoodId = 93;

    public static List<Food> Create() => new()
    {
        F(1, "Tavuk göğsü", 100, "g", 165, 31, 0, 3.6m, 0, 0, FoodCategory.ProteinSource, L | D, FoodAllergen.None, DietTag.None),
        F(2, "Hindi göğsü", 100, "g", 135, 29, 0, 1.5m, 0, 0, FoodCategory.ProteinSource, L | D, FoodAllergen.None, DietTag.None),
        F(3, "Somon", 100, "g", 208, 20, 0, 13, 0, 0, FoodCategory.ProteinSource, L | D, FoodAllergen.Fish, DietTag.None),
        F(4, "Ton balığı (suda)", 100, "g", 116, 26, 0, 1, 0, 0, FoodCategory.ProteinSource, L | D, FoodAllergen.Fish, DietTag.None),
        F(5, "Az yağlı dana kıyma", 100, "g", 176, 26, 0, 8, 0, 0, FoodCategory.ProteinSource, L | D, FoodAllergen.None, DietTag.None),
        F(6, "Yumurta", 1, "adet", 78, 6.3m, 0.6m, 5.3m, 0, 0.6m, FoodCategory.ProteinSource, B | S, FoodAllergen.Egg, DietTag.Vegetarian),
        F(7, "Lor peyniri", 100, "g", 98, 11, 3.4m, 4.3m, 0, 2.7m, FoodCategory.Dairy, B | S, FoodAllergen.Milk, DietTag.Vegetarian),
        F(8, "Beyaz peynir", 30, "g", 80, 5, 0.5m, 6.5m, 0, 0.5m, FoodCategory.Dairy, B, FoodAllergen.Milk, DietTag.Vegetarian),
        F(9, "Yoğurt (yarım yağlı)", 150, "g", 95, 8, 10, 2.5m, 0, 10, FoodCategory.Dairy, B | L | D | S, FoodAllergen.Milk, DietTag.Vegetarian),
        F(10, "Kefir", 200, "ml", 110, 7, 9, 5, 0, 9, FoodCategory.Dairy, B | S, FoodAllergen.Milk, DietTag.Vegetarian),
        F(11, "Tofu", 100, "g", 144, 15.7m, 3.5m, 8.7m, 2.3m, 0.6m, FoodCategory.ProteinSource, B | L | D, FoodAllergen.Soy, DietTag.Vegan),
        F(12, "Kırmızı mercimek (pişmiş)", 100, "g", 116, 9, 20, 0.4m, 7.9m, 1.8m, FoodCategory.Legume, L | D, FoodAllergen.None, DietTag.Vegan),
        F(13, "Nohut (pişmiş)", 100, "g", 164, 8.9m, 27, 2.6m, 7.6m, 4.8m, FoodCategory.Legume, L | D | S, FoodAllergen.None, DietTag.Vegan),
        F(14, "Kuru fasulye (pişmiş)", 100, "g", 127, 8.7m, 22.8m, 0.5m, 6.4m, 0.3m, FoodCategory.Legume, L | D, FoodAllergen.None, DietTag.Vegan),
        F(15, "Yulaf ezmesi", 40, "g", 150, 5.3m, 27, 2.8m, 4, 0.4m, FoodCategory.GrainStarch, B | S, FoodAllergen.Gluten, DietTag.Vegan),
        F(16, "Tam buğday ekmeği", 1, "dilim", 80, 4, 13.8m, 1.1m, 1.9m, 1.4m, FoodCategory.GrainStarch, B | L | D, FoodAllergen.Gluten, DietTag.Vegan),
        F(17, "Bulgur pilavı", 100, "g", 83, 3.1m, 18.6m, 0.2m, 4.5m, 0.1m, FoodCategory.GrainStarch, L | D, FoodAllergen.Gluten, DietTag.Vegan),
        F(18, "Pirinç pilavı", 100, "g", 130, 2.7m, 28, 0.3m, 0.4m, 0, FoodCategory.GrainStarch, L | D, FoodAllergen.None, DietTag.Vegan),
        F(19, "Tam buğday makarna", 100, "g", 124, 5.3m, 26.5m, 0.5m, 4.5m, 0.8m, FoodCategory.GrainStarch, L | D, FoodAllergen.Gluten, DietTag.Vegan),
        F(20, "Haşlanmış patates", 100, "g", 87, 1.9m, 20, 0.1m, 1.8m, 0.9m, FoodCategory.GrainStarch, L | D, FoodAllergen.None, DietTag.Vegan),
        F(21, "Karabuğday", 100, "g", 92, 3.4m, 19.9m, 0.6m, 2.7m, 0.9m, FoodCategory.GrainStarch, B | L | D, FoodAllergen.None, DietTag.Vegan),
        F(22, "Brokoli", 100, "g", 35, 2.4m, 7.2m, 0.4m, 3.3m, 1.4m, FoodCategory.Vegetable, L | D, FoodAllergen.None, DietTag.Vegan),
        F(23, "Ispanak", 100, "g", 23, 2.9m, 3.6m, 0.4m, 2.2m, 0.4m, FoodCategory.Vegetable, L | D, FoodAllergen.None, DietTag.Vegan),
        F(24, "Domates", 100, "g", 18, 0.9m, 3.9m, 0.2m, 1.2m, 2.6m, FoodCategory.Vegetable, B | L | D, FoodAllergen.None, DietTag.Vegan),
        F(25, "Salatalık", 100, "g", 15, 0.7m, 3.6m, 0.1m, 0.5m, 1.7m, FoodCategory.Vegetable, B | L | D | S, FoodAllergen.None, DietTag.Vegan),
        F(26, "Kabak", 100, "g", 17, 1.2m, 3.1m, 0.3m, 1, 2.5m, FoodCategory.Vegetable, L | D, FoodAllergen.None, DietTag.Vegan),
        F(27, "Elma", 1, "adet", 95, 0.5m, 25, 0.3m, 4.4m, 19, FoodCategory.Fruit, B | S, FoodAllergen.None, DietTag.Vegan),
        F(28, "Muz", 1, "adet", 105, 1.3m, 27, 0.4m, 3.1m, 14, FoodCategory.Fruit, B | S, FoodAllergen.None, DietTag.Vegan),
        F(29, "Çilek", 100, "g", 32, 0.7m, 7.7m, 0.3m, 2, 4.9m, FoodCategory.Fruit, B | S, FoodAllergen.None, DietTag.Vegan),
        F(30, "Ceviz", 15, "g", 98, 2.3m, 2, 9.8m, 1, 0.4m, FoodCategory.NutsSeedsFats, B | S, FoodAllergen.TreeNuts, DietTag.Vegan),
        F(31, "Badem", 15, "g", 87, 3.2m, 3.2m, 7.5m, 1.9m, 0.7m, FoodCategory.NutsSeedsFats, B | S, FoodAllergen.TreeNuts, DietTag.Vegan),
        F(32, "Zeytinyağı", 10, "ml", 88, 0, 0, 10, 0, 0, FoodCategory.NutsSeedsFats, L | D, FoodAllergen.None, DietTag.Vegan),
        F(33, "Fıstık ezmesi", 16, "g", 94, 4, 3, 8, 1, 1.5m, FoodCategory.NutsSeedsFats, B | S, FoodAllergen.Peanut, DietTag.Vegan),

        // Siniflandirilmamis kayitlar (mevcut katalogdaki gibi): plana girmemeli.
        new() { Id = UnclassifiedFoodId, Name = "Siniflandirilmamis tavuk", ServingSize = 100, ServingUnit = "g", Calories = 165, Protein = 31, Carbs = 0, Fat = 3.6m },
        new() { Id = 91, Name = "Kategori var, ogun yok", ServingSize = 100, ServingUnit = "g", Calories = 130, Protein = 2.7m, Carbs = 28, Fat = 0.3m, Category = FoodCategory.GrainStarch },
        new() { Id = 92, Name = "Diger kategori", ServingSize = 100, ServingUnit = "g", Calories = 50, Protein = 1, Carbs = 10, Fat = 0.5m, Category = FoodCategory.Other, MealSuitability = L | D },

        // Bildirilen kalori makrolarla tutarsiz: plana girmemeli.
        F(InconsistentEnergyFoodId, "Hatali kayit", 100, "g", 600, 1, 1, 1, 0, 0, FoodCategory.ProteinSource, B | L | D | S, FoodAllergen.None, DietTag.Vegan),
    };

    private static Food F(
        long id, string name, decimal servingSize, string unit, decimal kcal, decimal protein, decimal carbs, decimal fat,
        decimal fiber, decimal sugar, FoodCategory category, MealSuitability meals, FoodAllergen allergens, DietTag diet) => new()
    {
        Id = id,
        Name = name,
        ServingSize = servingSize,
        ServingUnit = unit,
        Calories = kcal,
        Protein = protein,
        Carbs = carbs,
        Fat = fat,
        Fiber = fiber,
        Sugar = sugar,
        Category = category,
        MealSuitability = meals,
        Allergens = allergens,
        DietTags = diet,
    };
}
