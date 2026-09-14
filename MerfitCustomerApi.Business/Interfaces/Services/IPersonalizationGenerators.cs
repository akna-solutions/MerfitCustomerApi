using MerfitCustomerApi.Business.Services.Personalization.Models;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin profil/hedef/ekipman verilerinden, tamamen kurallara dayali (deterministik,
/// yapay zeka/ML KULLANMAYAN) bir kisisel antrenman programi uretir. Yalnizca veri okur;
/// hicbir kaydi veritabanina yazmaz (kaydetme/transaction sorumlulugu
/// IPersonalizationJobProcessor'dadir).
/// </summary>
public interface IWorkoutPlanGenerator
{
    /// <summary>
    /// Verilen kullanici icin bir WorkoutPlan + WorkoutPlanDay + WorkoutPlanExercise graph'i uretir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">UserProfile bulunamazsa firlatilir.</exception>
    /// <exception cref="PersonalizationGenerationException">
    /// Kullanicinin kriterlerine uyan yeterli aday antrenman bulunamazsa firlatilir (orn. bos katalog).
    /// </exception>
    Task<WorkoutPlanGenerationResult> GenerateAsync(long userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Kullanicinin NutritionGoal hedeflerinden ve mevcut Food kataloğundan, tamamen kurallara
/// dayali (deterministik, yapay zeka/ML KULLANMAYAN) bir kisisel beslenme programi uretir.
/// Yalnizca veri okur; hicbir kaydi veritabanina yazmaz.
/// </summary>
public interface INutritionPlanGenerator
{
    /// <summary>
    /// Verilen kullanici icin bir NutritionPlan + NutritionPlanDay + NutritionPlanMeal +
    /// NutritionPlanMealItem graph'i uretir.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">UserProfile bulunamazsa firlatilir.</exception>
    /// <exception cref="PersonalizationGenerationException">
    /// Yeterli Food kaydi bulunamadigi icin hedeflere yakin bir menu uretilemezse firlatilir.
    /// </exception>
    Task<NutritionPlanGenerationResult> GenerateAsync(long userId, CancellationToken cancellationToken = default);
}
