using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Foods;
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

    private readonly MealLogWriter _mealLogWriter;

    public NutritionService(IUnitOfWork unitOfWork, INutritionCalculator nutritionCalculator, IMerfitScoreService scoreService, INotificationReminderCanceller? reminderCanceller = null)
    {
        _unitOfWork = unitOfWork;
        _nutritionCalculator = nutritionCalculator;
        _scoreService = scoreService;
        _mealLogWriter = new MealLogWriter(unitOfWork, scoreService, reminderCanceller);
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
        var mealType = MealLogWriter.ParseMealType(request.MealType, nameof(request.MealType));

        var food = await _unitOfWork.Repository<Food>().GetByIdAsync(request.FoodId, cancellationToken);
        if (food is null)
        {
            throw new NotFoundException(nameof(Food), request.FoodId);
        }

        var quantity = ResolveQuantity(food, request);

        var date = (request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var meal = await _mealLogWriter.GetOrCreateMealAsync(userId, date, mealType, cancellationToken);
        var mealItem = await _mealLogWriter.AddItemAsync(meal, food, quantity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Puan ozeti ve bu ogun icin bekleyen hatirlatmanin iptali (hata ogun kaydini bozmaz).
        await _mealLogWriter.AfterLoggedAsync(userId, date, mealType, cancellationToken);

        return new CustomerMealEntryDto
        {
            Id = mealItem.Id,
            Type = MapMealType(mealType),
            Name = food.Name,
            Calories = (int)Math.Round(mealItem.Calories),
        };
    }

    public async Task<CustomerMealItemDetailDto> GetMealItemAsync(long userId, long mealItemId, CancellationToken cancellationToken = default)
    {
        var (item, meal) = await GetOwnedMealItemAsync(userId, mealItemId, asNoTracking: true, cancellationToken);
        var food = await _unitOfWork.Repository<Food>().GetByIdAsync(item.FoodId, cancellationToken);
        var servingUnit = food?.ServingUnit ?? string.Empty;
        var weighable = ServingUnits.IsWeighable(servingUnit) && item.ServingSize > 0;
        var perServing = PerServing(item, food);

        return new CustomerMealItemDetailDto
        {
            Id = item.Id,
            Type = MapMealType(meal.MealType),
            Name = food?.Name ?? string.Empty,
            Calories = (int)Math.Round(item.Calories),
            Quantity = item.Quantity,
            Amount = weighable ? Math.Round(item.Quantity * item.ServingSize, 1) : null,
            Food = new CustomerFoodListItemDto
            {
                Id = item.FoodId,
                Name = food?.Name ?? string.Empty,
                Brand = food?.Brand,
                ServingSize = item.ServingSize,
                ServingUnit = servingUnit,
                Calories = (int)Math.Round(perServing.Calories),
                Protein = Math.Round(perServing.Protein, 2),
                Carbs = Math.Round(perServing.Carbs, 2),
                Fat = Math.Round(perServing.Fat, 2),
                ImageUrl = food?.ImageUrl,
            },
        };
    }

    public async Task<CustomerMealEntryDto> UpdateMealItemAsync(long userId, long mealItemId, UpdateMealItemRequest request, CancellationToken cancellationToken = default)
    {
        var (item, meal) = await GetOwnedMealItemAsync(userId, mealItemId, asNoTracking: false, cancellationToken);
        var food = await _unitOfWork.Repository<Food>().GetByIdAsync(item.FoodId, cancellationToken);

        decimal quantity;
        if (request.Amount is { } amount)
        {
            if (!ServingUnits.IsWeighable(food?.ServingUnit) || item.ServingSize <= 0)
            {
                throw new AppValidationException(nameof(request.Amount), "Bu besin gram/ml ile girilemez; porsiyon secin.");
            }
            quantity = amount / item.ServingSize;
        }
        else if (request.Quantity is { } requested && requested > 0)
        {
            quantity = requested;
        }
        else
        {
            throw new AppValidationException(nameof(request.Quantity), "Miktar girilmelidir.");
        }

        // Kalori/makrolar kayit anindaki porsiyon degerleriyle olceklenir (katalog sonradan degismis olabilir).
        var perServing = PerServing(item, food);
        item.Quantity = Math.Round(quantity, 2);
        item.Calories = Math.Round(perServing.Calories * quantity, 2);
        item.Protein = Math.Round(perServing.Protein * quantity, 2);
        item.Carbs = Math.Round(perServing.Carbs * quantity, 2);
        item.Fat = Math.Round(perServing.Fat * quantity, 2);
        item.Fiber = perServing.Fiber is { } fiber ? Math.Round(fiber * quantity, 2) : null;
        item.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<MealItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        return new CustomerMealEntryDto
        {
            Id = item.Id,
            Type = MapMealType(meal.MealType),
            Name = food?.Name ?? string.Empty,
            Calories = (int)Math.Round(item.Calories),
        };
    }

    public async Task DeleteMealItemAsync(long userId, long mealItemId, CancellationToken cancellationToken = default)
    {
        var (item, _) = await GetOwnedMealItemAsync(userId, mealItemId, asNoTracking: false, cancellationToken);

        _unitOfWork.Repository<MealItem>().Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);
    }

    /// <summary>Kaydi ve bagli oldugu ogunu getirir; kayit yoksa ya da baska kullaniciya aitse 404.</summary>
    private async Task<(MealItem Item, Meal Meal)> GetOwnedMealItemAsync(long userId, long mealItemId, bool asNoTracking, CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Repository<MealItem>()
            .GetQueryable(asNoTracking)
            .FirstOrDefaultAsync(mi => mi.Id == mealItemId, cancellationToken);
        var meal = item is null ? null : await _unitOfWork.Repository<Meal>().GetByIdAsync(item.MealId, cancellationToken);

        if (item is null || meal is null || meal.UserId != userId)
        {
            throw new NotFoundException(nameof(MealItem), mealItemId);
        }

        return (item, meal);
    }

    /// <summary>
    /// Kaydin porsiyon basina degerleri: kayitli toplamlar / carpan. Carpan 0 ise (bozuk kayit) besinin guncel degerleri.
    /// </summary>
    private static (decimal Calories, decimal Protein, decimal Carbs, decimal Fat, decimal? Fiber) PerServing(MealItem item, Food? food)
    {
        if (item.Quantity > 0)
        {
            return (item.Calories / item.Quantity, item.Protein / item.Quantity, item.Carbs / item.Quantity,
                item.Fat / item.Quantity, item.Fiber / item.Quantity);
        }

        return food is null
            ? (0, 0, 0, 0, null)
            : (food.Calories, food.Protein, food.Carbs, food.Fat, food.Fiber);
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

    private static string MapMealType(MealType mealType) => MealLogWriter.MapMealType(mealType);
}
