using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Services.Personalization.Models;

/// <summary>
/// WorkoutPlanGenerator'in ciktisi. Henuz veritabanina yazilmamis (Id = 0) entity graph'i tasir;
/// kaydetme ve transaction sorumlulugu PersonalizationJobProcessor'a aittir.
/// </summary>
public class WorkoutPlanGenerationResult
{
    /// <summary>Ana WorkoutPlan kaydi (henuz UserId disindaki Id'ler atanmamis).</summary>
    public required WorkoutPlan Plan { get; init; }

    /// <summary>Planin gunleri ve her gune ait kisisellestirilmis egzersizler.</summary>
    public required List<WorkoutPlanDayResult> Days { get; init; }

    /// <summary>
    /// Kullaniciya gosterilebilecek, tamamen kurallardan uretilmis (AI kullanilmadan) kisa
    /// aciklama. Orn: "Başlangıç seviyene, haftada 3 gün antrenman tercihine ve mevcut
    /// ekipmanlarına göre hazırlandı."
    /// </summary>
    public required string Summary { get; init; }
}

/// <summary>Tek bir WorkoutPlanDay ve o gune atanan kisisellestirilmis egzersizler.</summary>
public class WorkoutPlanDayResult
{
    public required WorkoutPlanDay Day { get; init; }
    public required List<WorkoutPlanExercise> Exercises { get; init; }
}

/// <summary>
/// NutritionPlanGenerator'in ciktisi. Henuz veritabanina yazilmamis (Id = 0) entity graph'i tasir.
/// </summary>
public class NutritionPlanGenerationResult
{
    public required NutritionPlan Plan { get; init; }
    public required List<NutritionPlanDayResult> Days { get; init; }
    public required string Summary { get; init; }
}

/// <summary>Tek bir NutritionPlanDay ve o gune atanan ogunler.</summary>
public class NutritionPlanDayResult
{
    public required NutritionPlanDay Day { get; init; }
    public required List<NutritionPlanMealResult> Meals { get; init; }
}

/// <summary>Tek bir NutritionPlanMeal ve icindeki besin ogeleri.</summary>
public class NutritionPlanMealResult
{
    public required NutritionPlanMeal Meal { get; init; }
    public required List<NutritionPlanMealItem> Items { get; init; }
}
