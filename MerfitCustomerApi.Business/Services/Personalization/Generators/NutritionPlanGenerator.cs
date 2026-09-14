using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Personalization.Generators;

/// <summary>
/// INutritionPlanGenerator'in varsayilan implementasyonu. Tamamen kurallara dayali (deterministik)
/// bir dagitim + olcekleme algoritmasi kullanir; yapay zeka/ML KULLANMAZ.
///
/// Algoritma ozet:
/// 1) NutritionGoal'daki gunluk kalori hedefi, options'taki ogun yuzdelerine gore
///    Breakfast/Lunch/Dinner/Snack arasinda dagitilir.
/// 2) Food katalogu, katalogdaki Id sirasina gore (Random KULLANILMAZ) 4 oguna round-robin
///    olarak bolunur; boylece her ogun farkli ama deterministik bir besin kumesi alir.
/// 3) Her ogun icin secilen besinlerin toplam (baz porsiyondaki) kalorisi hesaplanir ve TEK BIR
///    olcekleme katsayisi (multiplier) ile o ogunun kalori hedefine getirilir - katsayi
///    options'taki Min/MaxPortionMultiplier ile sinirlandirilir (asiri kucuk/buyuk porsiyon
///    onerilmez). Protein/karbonhidrat/yag degerleri ayni katsayiyla olceklenir (besinin dogal
///    makro oranini korur).
/// 4) Ayni ogun sablonu haftanin her gunune (7 gun) uygulanir; boylece her gun icin ayri, tekil
///    entity kopyalari uretilir.
///
/// Not: Kullanici modelinde alerji/diyet kisitlamasi alani bulunmadigi icin (bkz. Faz 2 raporu)
/// bu fazda boyle bir filtreleme uygulanmaz; ileride eklenirse bu generator'a bir filtre adimi
/// olarak entegre edilebilir.
/// </summary>
public class NutritionPlanGenerator : INutritionPlanGenerator
{
    private static readonly (MealType Type, string Name, TimeOnly PlannedTime)[] MealDefinitions =
    {
        (MealType.Breakfast, "Kahvaltı", new TimeOnly(8, 0)),
        (MealType.Lunch, "Öğle Yemeği", new TimeOnly(13, 0)),
        (MealType.Dinner, "Akşam Yemeği", new TimeOnly(19, 0)),
        (MealType.Snack, "Ara Öğün", new TimeOnly(16, 0)),
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly NutritionPlanGenerationOptions _options;

    public NutritionPlanGenerator(IUnitOfWork unitOfWork, IOptions<NutritionPlanGenerationOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<NutritionPlanGenerationResult> GenerateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var nutritionGoal = await _unitOfWork.Repository<NutritionGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (nutritionGoal is null)
        {
            throw new NotFoundException(nameof(NutritionGoal), userId);
        }

        var foods = await _unitOfWork.Repository<Food>()
            .GetQueryable()
            .OrderBy(f => f.Id) // deterministik siralama; Random KULLANILMAZ
            .ToListAsync(cancellationToken);

        if (foods.Count == 0)
        {
            throw new PersonalizationGenerationException("Besin kataloğunda hiç kayıt bulunamadığı için beslenme programı oluşturulamadı.");
        }

        var mealPercentages = new[]
        {
            _options.BreakfastCaloriePercentage,
            _options.LunchCaloriePercentage,
            _options.DinnerCaloriePercentage,
            _options.SnackCaloriePercentage,
        };

        // Ogun sablonlari bir kere hesaplanir, sonra her gun icin ayri entity kopyalari uretilir.
        var mealTemplates = new List<(NutritionPlanMeal Meal, List<NutritionPlanMealItem> Items)>();
        for (var mealIndex = 0; mealIndex < MealDefinitions.Length; mealIndex++)
        {
            var (mealType, name, plannedTime) = MealDefinitions[mealIndex];
            var mealCalorieTarget = nutritionGoal.DailyCalories * mealPercentages[mealIndex];

            var bucket = foods.Where((_, idx) => idx % MealDefinitions.Length == mealIndex).ToList();
            if (bucket.Count == 0)
            {
                bucket = foods;
            }

            var selected = bucket.Take(_options.MaxItemsPerMeal).ToList();
            var items = BuildMealItems(selected, mealCalorieTarget);

            var meal = new NutritionPlanMeal
            {
                MealType = mealType,
                Name = name,
                PlannedTime = plannedTime,
                CreatedAt = DateTime.UtcNow,
            };

            mealTemplates.Add((meal, items));
        }

        var days = new List<NutritionPlanDayResult>();
        foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>())
        {
            var meals = mealTemplates.Select(t => new NutritionPlanMealResult
            {
                Meal = CloneMeal(t.Meal),
                Items = t.Items.Select(CloneItem).ToList(),
            }).ToList();

            days.Add(new NutritionPlanDayResult
            {
                Day = new NutritionPlanDay
                {
                    DayOfWeek = dayOfWeek,
                    CreatedAt = DateTime.UtcNow,
                },
                Meals = meals,
            });
        }

        var summary = BuildSummary(nutritionGoal);

        var plan = new NutritionPlan
        {
            UserId = userId,
            Name = "Kişisel Beslenme Programı",
            // WorkoutPlanGenerator'daki ayni yaklasim: kalici Reason/Summary metni icin yeni bir
            // kolon eklemek yerine mevcut Description alani yeniden kullanilir.
            Description = summary,
            StartDate = DateTime.UtcNow.Date,
            EndDate = null,
            IsActive = true,
            IsAiGenerated = false,
            CreatedAt = DateTime.UtcNow,
        };

        return new NutritionPlanGenerationResult
        {
            Plan = plan,
            Days = days,
            Summary = summary,
        };
    }

    /// <summary>
    /// Secilen besinlerin baz porsiyondaki toplam kalorisini ogun hedefine getirecek TEK bir
    /// olcekleme katsayisi hesaplar (options'taki Min/MaxPortionMultiplier ile sinirli) ve
    /// bu katsayiyla her besinin miktar/kalori/makro degerlerini (snapshot olarak) uretir.
    /// </summary>
    private List<NutritionPlanMealItem> BuildMealItems(List<Food> selectedFoods, decimal mealCalorieTarget)
    {
        if (selectedFoods.Count == 0)
        {
            return new List<NutritionPlanMealItem>();
        }

        var baseTotalCalories = selectedFoods.Sum(f => f.Calories);
        var rawMultiplier = baseTotalCalories <= 0 ? 1m : mealCalorieTarget / baseTotalCalories;
        var multiplier = Math.Clamp(rawMultiplier, _options.MinPortionMultiplier, _options.MaxPortionMultiplier);

        return selectedFoods.Select(food => new NutritionPlanMealItem
        {
            FoodId = food.Id,
            Quantity = Math.Round(food.ServingSize * multiplier, 1),
            ServingUnit = food.ServingUnit,
            Calories = Math.Round(food.Calories * multiplier, 0),
            Protein = Math.Round(food.Protein * multiplier, 1),
            Carbs = Math.Round(food.Carbs * multiplier, 1),
            Fat = Math.Round(food.Fat * multiplier, 1),
            CreatedAt = DateTime.UtcNow,
        }).ToList();
    }

    private static NutritionPlanMeal CloneMeal(NutritionPlanMeal source) => new()
    {
        MealType = source.MealType,
        Name = source.Name,
        PlannedTime = source.PlannedTime,
        Notes = source.Notes,
        CreatedAt = DateTime.UtcNow,
    };

    private static NutritionPlanMealItem CloneItem(NutritionPlanMealItem source) => new()
    {
        FoodId = source.FoodId,
        Quantity = source.Quantity,
        ServingUnit = source.ServingUnit,
        Calories = source.Calories,
        Protein = source.Protein,
        Carbs = source.Carbs,
        Fat = source.Fat,
        CreatedAt = DateTime.UtcNow,
    };

    private static string BuildSummary(NutritionGoal goal)
    {
        return $"Günlük yaklaşık {goal.DailyCalories:0} kalori, {goal.ProteinTarget:0}g protein hedefine göre " +
               "kahvaltı, öğle, akşam ve ara öğün olarak hazırlandı.";
    }
}
