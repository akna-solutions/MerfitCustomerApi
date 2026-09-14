using MerfitCustomerApi.Business.Dtos.Customer.NutritionPlans;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Personalization;

/// <summary>
/// IMyNutritionPlanService'in varsayilan implementasyonu. Yalnizca okuma yapar; gunler/ogunler/
/// besinler toplu (batched) sorgularla cekilir (N+1 onlenir), AsNoTracking kullanilir.
/// </summary>
public class MyNutritionPlanService : IMyNutritionPlanService
{
    private readonly IUnitOfWork _unitOfWork;

    public MyNutritionPlanService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerNutritionPlanDto?> GetActivePlanAsync(long userId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<NutritionPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var days = await _unitOfWork.Repository<NutritionPlanDay>()
            .GetQueryable()
            .Where(d => d.NutritionPlanId == plan.Id)
            .OrderBy(d => d.DayOfWeek)
            .ToListAsync(cancellationToken);

        var dayDtos = await BuildDayDtosAsync(days, cancellationToken);

        return new CustomerNutritionPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            IsActive = plan.IsActive,
            Summary = plan.Description,
            Days = dayDtos,
        };
    }

    public async Task<CustomerTodayNutritionPlanResponse> GetTodayAsync(long userId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<NutritionPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (plan is null)
        {
            return new CustomerTodayNutritionPlanResponse { HasActivePlan = false, HasPlanToday = false };
        }

        var today = DateTime.UtcNow.DayOfWeek;
        var day = await _unitOfWork.Repository<NutritionPlanDay>()
            .FirstOrDefaultAsync(d => d.NutritionPlanId == plan.Id && d.DayOfWeek == today, cancellationToken);
        if (day is null)
        {
            return new CustomerTodayNutritionPlanResponse { HasActivePlan = true, HasPlanToday = false };
        }

        var dayDtos = await BuildDayDtosAsync(new List<NutritionPlanDay> { day }, cancellationToken);

        return new CustomerTodayNutritionPlanResponse
        {
            HasActivePlan = true,
            HasPlanToday = true,
            Day = dayDtos.FirstOrDefault(),
        };
    }

    private async Task<List<CustomerNutritionPlanDayDto>> BuildDayDtosAsync(
        List<NutritionPlanDay> days,
        CancellationToken cancellationToken)
    {
        if (days.Count == 0)
        {
            return new List<CustomerNutritionPlanDayDto>();
        }

        var dayIds = days.Select(d => d.Id).ToList();

        var meals = await _unitOfWork.Repository<NutritionPlanMeal>()
            .GetQueryable()
            .Where(m => dayIds.Contains(m.NutritionPlanDayId))
            .ToListAsync(cancellationToken);

        var mealIds = meals.Select(m => m.Id).ToList();

        var items = mealIds.Count == 0
            ? new List<NutritionPlanMealItem>()
            : await _unitOfWork.Repository<NutritionPlanMealItem>()
                .GetQueryable()
                .Where(i => mealIds.Contains(i.NutritionPlanMealId))
                .ToListAsync(cancellationToken);

        var foodIds = items.Select(i => i.FoodId).Distinct().ToList();
        var foodNames = foodIds.Count == 0
            ? new Dictionary<long, string>()
            : await _unitOfWork.Repository<Food>()
                .GetQueryable()
                .Where(f => foodIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        return days.Select(day =>
        {
            var mealsForDay = meals.Where(m => m.NutritionPlanDayId == day.Id);

            return new CustomerNutritionPlanDayDto
            {
                DayOfWeek = day.DayOfWeek.ToString(),
                Meals = mealsForDay.Select(meal =>
                {
                    var itemDtos = items
                        .Where(i => i.NutritionPlanMealId == meal.Id)
                        .Select(i => new CustomerNutritionPlanMealItemDto
                        {
                            FoodId = i.FoodId,
                            FoodName = foodNames.GetValueOrDefault(i.FoodId, string.Empty),
                            Quantity = i.Quantity,
                            ServingUnit = i.ServingUnit,
                            Calories = (int)Math.Round(i.Calories),
                            Protein = i.Protein,
                            Carbs = i.Carbs,
                            Fat = i.Fat,
                        })
                        .ToList();

                    return new CustomerNutritionPlanMealDto
                    {
                        MealType = meal.MealType.ToString(),
                        Name = meal.Name,
                        PlannedTime = meal.PlannedTime?.ToString("HH:mm"),
                        Notes = meal.Notes,
                        Items = itemDtos,
                        TotalCalories = itemDtos.Sum(i => i.Calories),
                        TotalProtein = itemDtos.Sum(i => i.Protein),
                        TotalCarbs = itemDtos.Sum(i => i.Carbs),
                        TotalFat = itemDtos.Sum(i => i.Fat),
                    };
                }).ToList(),
            };
        }).ToList();
    }
}
