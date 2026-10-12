using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// INotificationEligibilityService'in varsayilan implementasyonu. "Bugunun antrenmani" kurali
/// MyPlanService.GetTodayAsync ile aynidir (aktif plan + haftanin gunu); tek fark gunun kullanicinin
/// kendi saat dilimine gore belirlenmesidir.
/// </summary>
public class NotificationEligibilityService : INotificationEligibilityService
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationEligibilityService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<HashSet<long>> GetUsersMissingMealAsync(IReadOnlyCollection<long> userIds, DateOnly localDate, MealType mealType, bool onlyUsersWithPlan, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<long>();
        }

        var activePlans = await _unitOfWork.Repository<NutritionPlan>().GetQueryable()
            .Where(p => ids.Contains(p.UserId) && p.IsActive)
            .Select(p => new { p.Id, p.UserId })
            .ToListAsync(cancellationToken);
        var planIds = activePlans.Select(p => p.Id).ToList();

        var day = localDate.DayOfWeek;
        var days = _unitOfWork.Repository<NutritionPlanDay>().GetQueryable();
        var plannedPlanIds = planIds.Count == 0
            ? new List<long>()
            : await _unitOfWork.Repository<NutritionPlanMeal>().GetQueryable()
                .Where(m => m.MealType == mealType)
                .Join(days.Where(d => planIds.Contains(d.NutritionPlanId) && d.DayOfWeek == day),
                    m => m.NutritionPlanDayId, d => d.Id, (m, d) => d.NutritionPlanId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var usersWithPlan = activePlans.Select(p => p.UserId).ToHashSet();
        var usersWithPlannedMeal = activePlans.Where(p => plannedPlanIds.Contains(p.Id)).Select(p => p.UserId).ToHashSet();

        var expected = ids
            .Where(id => usersWithPlannedMeal.Contains(id) || (!onlyUsersWithPlan && !usersWithPlan.Contains(id)))
            .ToList();
        if (expected.Count == 0)
        {
            return new HashSet<long>();
        }

        // Ogun tarihi istemcinin yerel tarihi olarak UTC gece yarisinda saklanir (bkz. NutritionService.LogMealItemAsync).
        var date = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var items = _unitOfWork.Repository<MealItem>().GetQueryable();
        var loggedUserIds = await _unitOfWork.Repository<Meal>().GetQueryable()
            .Where(m => expected.Contains(m.UserId) && m.Date == date && m.MealType == mealType)
            .Where(m => items.Any(i => i.MealId == m.Id))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return expected.Except(loggedUserIds).ToHashSet();
    }

    public async Task<Dictionary<long, ScheduledWorkout>> GetUsersWithIncompleteWorkoutAsync(IReadOnlyCollection<long> userIds, DateOnly localDate, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<long, ScheduledWorkout>();
        }

        var day = localDate.DayOfWeek;
        var plans = _unitOfWork.Repository<WorkoutPlan>().GetQueryable().Where(p => ids.Contains(p.UserId) && p.IsActive);
        var scheduled = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable()
            .Where(d => d.DayOfWeek == day)
            .Join(plans, d => d.WorkoutPlanId, p => p.Id, (d, p) => new { p.UserId, DayId = d.Id, d.WorkoutId, d.Order })
            .ToListAsync(cancellationToken);
        if (scheduled.Count == 0)
        {
            return new Dictionary<long, ScheduledWorkout>();
        }

        var scheduledUserIds = scheduled.Select(s => s.UserId).Distinct().ToList();
        var completedUserIds = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => scheduledUserIds.Contains(s.UserId)
                && s.Status == WorkoutSessionStatus.Completed
                && s.CompletedAt >= dayStartUtc
                && s.CompletedAt < dayEndUtc)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var workoutIds = scheduled.Select(s => s.WorkoutId).Distinct().ToList();
        var names = await _unitOfWork.Repository<Workout>().GetQueryable()
            .Where(w => workoutIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Title })
            .ToDictionaryAsync(w => w.Id, w => w.Title, cancellationToken);

        return scheduled
            .Where(s => !completedUserIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.OrderBy(x => x.Order).First();
                return new ScheduledWorkout(first.DayId, names.GetValueOrDefault(first.WorkoutId, string.Empty));
            });
    }
}
