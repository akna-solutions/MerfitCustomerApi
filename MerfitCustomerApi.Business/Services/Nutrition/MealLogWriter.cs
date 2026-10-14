using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// Gunluge besin kaydinin tek yazim yolu: gun + ogun turu icin Meal kaydini bulur ya da olusturur, Food'un o anki
/// degerleriyle MealItem anlik goruntusu ekler; kayittan sonra MB FIT puanini yeniler ve o ogunun hatirlatmasini
/// iptal eder. NutritionService (tek besin ekleme) ve FoodAnalysisService (fotograftan onaylanan ogun) ayni kurallari
/// kullansin diye ayrildi.
/// </summary>
public sealed class MealLogWriter
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;
    private readonly INotificationReminderCanceller? _reminderCanceller;

    public MealLogWriter(IUnitOfWork unitOfWork, IMerfitScoreService scoreService, INotificationReminderCanceller? reminderCanceller)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
        _reminderCanceller = reminderCanceller;
    }

    public static MealType ParseMealType(string? value, string field = "MealType")
    {
        if (!Enum.TryParse<MealType>(value, ignoreCase: true, out var mealType) || !Enum.IsDefined(mealType))
        {
            throw new AppValidationException(field, "Gecersiz ogun turu.");
        }
        return mealType;
    }

    /// <summary>Gun + ogun turu icin Meal kaydini getirir; yoksa olusturup kaydeder.</summary>
    public async Task<Meal> GetOrCreateMealAsync(long userId, DateTime date, MealType mealType, CancellationToken cancellationToken)
    {
        var meal = await _unitOfWork.Repository<Meal>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Date == date && m.MealType == mealType, cancellationToken);

        if (meal is null)
        {
            meal = new Meal
            {
                UserId = userId,
                Date = date,
                MealType = mealType,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<Meal>().AddAsync(meal, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return meal;
    }

    /// <summary>Food'un porsiyon basina degerlerini carpanla carpip MealItem ekler (SaveChanges cagirmaz).</summary>
    public async Task<MealItem> AddItemAsync(Meal meal, Food food, decimal quantity, CancellationToken cancellationToken)
    {
        var mealItem = new MealItem
        {
            MealId = meal.Id,
            FoodId = food.Id,
            Quantity = Math.Round(quantity, 2),
            ServingSize = food.ServingSize,
            Calories = Math.Round(food.Calories * quantity, 2),
            Protein = Math.Round(food.Protein * quantity, 2),
            Carbs = Math.Round(food.Carbs * quantity, 2),
            Fat = Math.Round(food.Fat * quantity, 2),
            Fiber = food.Fiber.HasValue ? Math.Round(food.Fiber.Value * quantity, 2) : null,
            CreatedAt = DateTime.UtcNow,
        };
        await _unitOfWork.Repository<MealItem>().AddAsync(mealItem, cancellationToken);
        return mealItem;
    }

    /// <summary>
    /// Kayit sonrasi yan etkiler: MB FIT puan ozeti ve ogun hatirlatmasinin iptali. Ikisi de hata verse bile
    /// ogun kaydi bozulmaz; bu yuzden kayit transaction'i tamamlandiktan sonra cagrilmalidir.
    /// </summary>
    public async Task AfterLoggedAsync(long userId, DateTime date, MealType mealType, CancellationToken cancellationToken)
    {
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        await NotificationReminderCancellation.TryRunAsync(_unitOfWork, _reminderCanceller,
            c => c.CancelMealRemindersAsync(userId, DateOnly.FromDateTime(date), mealType, cancellationToken), cancellationToken);
    }

    public static string MapMealType(MealType mealType) => mealType switch
    {
        MealType.Breakfast => "Kahvaltı",
        MealType.Lunch => "Öğle Yemeği",
        MealType.Dinner => "Akşam Yemeği",
        MealType.Snack => "Atıştırmalık",
        _ => mealType.ToString(),
    };
}
