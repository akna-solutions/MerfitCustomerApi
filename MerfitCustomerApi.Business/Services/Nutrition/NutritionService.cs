using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// INutritionService'in varsayilan implementasyonu. Mevcut Food/Meal/MealItem/NutritionGoal/
/// WaterLog/UserProfile entity'lerini oldugu gibi kullanir, yeni entity eklemez.
/// </summary>
public class NutritionService : INutritionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INutritionCalculator _nutritionCalculator;

    private readonly IMerfitScoreService _scoreService;

    private readonly INotificationReminderCanceller? _reminderCanceller;

    public NutritionService(IUnitOfWork unitOfWork, INutritionCalculator nutritionCalculator, IMerfitScoreService scoreService, INotificationReminderCanceller? reminderCanceller = null)
    {
        _unitOfWork = unitOfWork;
        _nutritionCalculator = nutritionCalculator;
        _scoreService = scoreService;
        _reminderCanceller = reminderCanceller;
    }

    public async Task<CustomerNutritionResponse> GetDailyNutritionAsync(long userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var goal = await GetOrCreateGoalAsync(userId, cancellationToken);

        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var meals = await _unitOfWork.Repository<Meal>()
            .GetQueryable()
            .Where(m => m.UserId == userId && m.Date >= dayStart && m.Date < dayEnd)
            .ToListAsync(cancellationToken);

        var mealIds = meals.Select(m => m.Id).ToList();

        var items = mealIds.Count == 0
            ? new List<MealItem>()
            : await _unitOfWork.Repository<MealItem>()
                .GetQueryable()
                .Where(mi => mealIds.Contains(mi.MealId))
                .ToListAsync(cancellationToken);

        var foodIds = items.Select(i => i.FoodId).Distinct().ToList();
        var foodNames = foodIds.Count == 0
            ? new Dictionary<long, string>()
            : await _unitOfWork.Repository<Food>()
                .GetQueryable()
                .Where(f => foodIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        var mealsById = meals.ToDictionary(m => m.Id);

        var mealEntries = items
            .Select(item => new CustomerMealEntryDto
            {
                Id = item.Id,
                Type = MapMealType(mealsById[item.MealId].MealType),
                Name = foodNames.GetValueOrDefault(item.FoodId, string.Empty),
                Calories = (int)Math.Round(item.Calories),
            })
            .ToList();

        var waterConsumedMl = await _unitOfWork.Repository<WaterLog>()
            .GetQueryable()
            .Where(w => w.UserId == userId && w.Date >= dayStart && w.Date < dayEnd)
            .SumAsync(w => (decimal?)w.AmountMl, cancellationToken) ?? 0;

        var response = new CustomerNutritionResponse
        {
            HasLoggedFirstMeal = mealEntries.Count > 0,
            DailyCalories = (int)Math.Round(goal.DailyCalories),
            Macros = new CustomerMacroOverviewDto
            {
                Protein = new CustomerMacroTargetDto
                {
                    Consumed = (int)Math.Round(items.Sum(i => i.Protein)),
                    Target = (int)Math.Round(goal.ProteinTarget),
                },
                Carbs = new CustomerMacroTargetDto
                {
                    Consumed = (int)Math.Round(items.Sum(i => i.Carbs)),
                    Target = (int)Math.Round(goal.CarbsTarget),
                },
                Fats = new CustomerMacroTargetDto
                {
                    Consumed = (int)Math.Round(items.Sum(i => i.Fat)),
                    Target = (int)Math.Round(goal.FatTarget),
                },
            },
            Water = new CustomerWaterDto
            {
                ConsumedL = Math.Round(waterConsumedMl / 1000m, 2),
                TargetL = Math.Round(goal.WaterTargetMl / 1000m, 2),
            },
            Meals = mealEntries,
        };
        response.Insight = NutritionInsightEvaluator.Evaluate(response);
        return response;
    }

    public async Task<CustomerMealEntryDto> LogMealItemAsync(long userId, LogMealItemRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MealType>(request.MealType, ignoreCase: true, out var mealType))
        {
            throw new AppValidationException(nameof(request.MealType), "Gecersiz ogun turu.");
        }

        var food = await _unitOfWork.Repository<Food>().GetByIdAsync(request.FoodId, cancellationToken);
        if (food is null)
        {
            throw new NotFoundException(nameof(Food), request.FoodId);
        }

        var quantity = ResolveQuantity(food, request);

        var date = (request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        // Bu ogun icin bekleyen hatirlatmayi iptal et (hata ogun kaydini bozmaz).
        await NotificationReminderCancellation.TryRunAsync(_unitOfWork, _reminderCanceller,
            c => c.CancelMealRemindersAsync(userId, DateOnly.FromDateTime(date), mealType, cancellationToken), cancellationToken);

        return new CustomerMealEntryDto
        {
            Id = mealItem.Id,
            Type = MapMealType(mealType),
            Name = food.Name,
            Calories = (int)Math.Round(mealItem.Calories),
        };
    }

    public async Task<CustomerWaterDto> LogWaterAsync(long userId, LogWaterRequest request, CancellationToken cancellationToken = default)
    {
        var date = (request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var log = new WaterLog
        {
            UserId = userId,
            Date = date,
            AmountMl = request.AmountMl,
            CreatedAt = DateTime.UtcNow,
        };
        await _unitOfWork.Repository<WaterLog>().AddAsync(log, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        var goal = await GetOrCreateGoalAsync(userId, cancellationToken);

        var dayEnd = date.AddDays(1);
        var consumedMl = await _unitOfWork.Repository<WaterLog>()
            .GetQueryable()
            .Where(w => w.UserId == userId && w.Date >= date && w.Date < dayEnd)
            .SumAsync(w => (decimal?)w.AmountMl, cancellationToken) ?? 0;

        return new CustomerWaterDto
        {
            ConsumedL = Math.Round(consumedMl / 1000m, 2),
            TargetL = Math.Round(goal.WaterTargetMl / 1000m, 2),
        };
    }

    /// <summary>
    /// Kullanicinin NutritionGoal kaydini getirir; hic yoksa UserProfile verilerinden
    /// (Mifflin-St Jeor BMR + aktivite carpani + hedefe gore ayarlama) hesaplayip kalici olarak olusturur.
    /// Boylece "sahte"/sabit bir varsayilan yerine kullaniciya ozel, gercek bir ilk deger uretilir.
    /// </summary>
    private async Task<NutritionGoal> GetOrCreateGoalAsync(long userId, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.Repository<NutritionGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var calculated = _nutritionCalculator.Calculate(profile);
        var goal = new NutritionGoal
        {
            UserId = profile.UserId,
            DailyCalories = calculated.DailyCalories,
            ProteinTarget = calculated.ProteinTarget,
            CarbsTarget = calculated.CarbsTarget,
            FatTarget = calculated.FatTarget,
            WaterTargetMl = calculated.WaterTargetMl,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<NutritionGoal>().AddAsync(goal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return goal;
    }

    /// <summary>
    /// Porsiyon carpanini belirler: Amount (g/ml) verildiyse Amount / ServingSize, yoksa Quantity.
    /// Besin degerleri bu carpanin yuvarlanmamis hali ile hesaplanir.
    /// </summary>
    private static decimal ResolveQuantity(Food food, LogMealItemRequest request)
    {
        if (request.Amount is not { } amount)
        {
            return request.Quantity <= 0 ? 1 : request.Quantity;
        }

        if (!ServingUnits.IsWeighable(food.ServingUnit) || food.ServingSize <= 0)
        {
            throw new AppValidationException(nameof(request.Amount), "Bu besin gram/ml ile girilemez; porsiyon secin.");
        }

        return amount / food.ServingSize;
    }

    private static string MapMealType(MealType mealType) => mealType switch
    {
        MealType.Breakfast => "Kahvaltı",
        MealType.Lunch => "Öğle Yemeği",
        MealType.Dinner => "Akşam Yemeği",
        MealType.Snack => "Atıştırmalık",
        _ => mealType.ToString(),
    };
}
