using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Asistan baglamini guvenilir kaynaklardan olusturur: gunluk tuketim ve hedefler beslenme ekraniyla ayni
/// servisten (INutritionService.GetDailyNutritionAsync, ayni NutritionGoal ve ayni gun sinirlari), profil
/// ve antrenman bilgisi veritabanindan. Hesaplama tekrarlanmaz.
/// </summary>
public class NutritionAssistantContextBuilder
{
    /// <summary>Baglama eklenen en fazla dislanan besin adi.</summary>
    private const int MaxExcludedFoodNames = 15;

    private readonly IUnitOfWork _unitOfWork;
    private readonly INutritionService _nutritionService;

    public NutritionAssistantContextBuilder(IUnitOfWork unitOfWork, INutritionService nutritionService)
    {
        _unitOfWork = unitOfWork;
        _nutritionService = nutritionService;
    }

    /// <param name="localTime">Gun kullanicinin bugunu ise yerel saat; degilse null.</param>
    public async Task<NutritionAssistantContext> BuildAsync(long userId, DateOnly date, TimeOnly? localTime, CancellationToken cancellationToken)
    {
        var day = await _nutritionService.GetDailyNutritionAsync(userId, date, cancellationToken);

        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var excludedIds = FoodPreferenceFormat.ParseExcludedFoodIds(profile?.ExcludedFoodIds).ToList();
        var excludedNames = excludedIds.Count == 0
            ? new List<string>()
            : await _unitOfWork.Repository<Food>().GetQueryable()
                .Where(f => excludedIds.Contains(f.Id))
                .OrderBy(f => f.Name)
                .Select(f => f.Name)
                .Take(MaxExcludedFoodNames)
                .ToListAsync(cancellationToken);

        // Beslenme gunuyle ayni (UTC) gun sinirlari.
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var sessions = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.UserId == userId && s.StartedAt >= dayStart && s.StartedAt < dayEnd)
            .Select(s => new { s.Status, s.DurationSeconds, s.CaloriesBurned })
            .ToListAsync(cancellationToken);
        var completed = sessions.Where(s => s.Status == WorkoutSessionStatus.Completed).ToList();

        var insight = NutritionInsightEvaluator.Evaluate(day);

        return new NutritionAssistantContext
        {
            Date = date,
            LocalTime = localTime,
            FirstName = string.IsNullOrWhiteSpace(profile?.FirstName) ? null : profile!.FirstName.Trim(),
            Day = day,
            Insight = insight,
            Goal = profile?.Goal,
            Gender = profile?.Gender,
            Age = NutritionAssistantContext.AgeOn(profile?.DateOfBirth, date),
            HeightCm = profile?.HeightCm,
            WeightKg = profile?.WeightKg,
            TargetWeightKg = profile?.TargetWeightKg,
            ActivityLevel = profile?.ActivityLevel,
            TrainingDaysPerWeek = profile?.TrainingDaysPerWeek,
            DietaryPreference = profile?.DietaryPreference,
            Allergies = profile?.Allergies,
            ExcludedFoods = excludedNames,
            CompletedWorkouts = completed.Count,
            WorkoutInProgress = sessions.Any(s => s.Status is WorkoutSessionStatus.Started or WorkoutSessionStatus.Paused),
            WorkoutMinutes = (int)Math.Round(completed.Sum(s => s.DurationSeconds ?? 0) / 60.0),
            WorkoutCaloriesBurned = (int)Math.Round(completed.Sum(s => s.CaloriesBurned ?? 0)),
        };
    }
}
