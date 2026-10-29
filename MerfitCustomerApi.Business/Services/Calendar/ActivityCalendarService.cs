using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Calendar;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Calendar;

/// <summary>
/// IActivityCalendarService'in varsayilan implementasyonu. Yeni tablo eklemez; WorkoutSession, Meal/MealItem,
/// NutritionGoal ve aktif WorkoutPlan verisinden gunluk ozet uretir. Tum aralik sabit sayida toplu sorguyla
/// okunur (gun basina sorgu yoktur).
///
/// Gun siniri: ogunler istemcinin sectigi yerel gune (Meal.Date) kaydedildigi icin dogrudan o tarihle,
/// antrenmanlar ise CompletedAt (UTC) kullanicinin saat dilimine cevrilerek gune atanir.
/// </summary>
public class ActivityCalendarService : IActivityCalendarService
{
    /// <summary>Tek istekte istenebilecek en uzun aralik (aylik gorunum + komsu haftalar).</summary>
    public const int MaxRangeDays = 62;

    private static readonly DateOnly MinDate = new(2000, 1, 1);
    private static readonly DateOnly MaxDate = new(2100, 12, 31);

    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly TimeProvider _timeProvider;

    public ActivityCalendarService(IUnitOfWork unitOfWork, IOptions<NotificationOptions>? notificationOptions = null, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions?.Value ?? new NotificationOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CustomerActivityCalendarResponse> GetCalendarAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from < MinDate || to > MaxDate)
        {
            throw new AppValidationException(nameof(from), "Gecerli bir tarih araligi girin (from ve to, yyyy-MM-dd).");
        }

        if (to < from)
        {
            throw new AppValidationException(nameof(to), "Bitis tarihi baslangic tarihinden once olamaz.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            throw new AppValidationException(nameof(to), $"En fazla {MaxRangeDays} gunluk aralik istenebilir.");
        }

        var preference = await _unitOfWork.Repository<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var zone = NotificationClock.Resolve(preference?.TimeZone, _notificationOptions);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(NotificationClock.ToLocal(nowUtc, zone));

        var (rangeStartUtc, _) = NotificationClock.LocalDayBoundsUtc(from, zone);
        var (_, rangeEndUtc) = NotificationClock.LocalDayBoundsUtc(to, zone);

        // ---- Antrenmanlar ----
        var sessions = await _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .Where(s => s.UserId == userId
                && s.Status == WorkoutSessionStatus.Completed
                && s.CompletedAt != null
                && s.CompletedAt >= rangeStartUtc
                && s.CompletedAt < rangeEndUtc)
            .Select(s => new { s.Id, s.WorkoutId, CompletedAt = s.CompletedAt!.Value, s.DurationSeconds, s.CaloriesBurned })
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var workoutIds = sessions.Select(s => s.WorkoutId).Distinct().ToList();

        var workoutTitles = workoutIds.Count == 0
            ? new Dictionary<long, string>()
            : await _unitOfWork.Repository<Workout>()
                .GetQueryable()
                .Where(w => workoutIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Title, cancellationToken);

        var setCounts = sessionIds.Count == 0
            ? new Dictionary<long, (int Sets, int Exercises)>()
            : (await (
                    from log in _unitOfWork.Repository<WorkoutSetLog>().GetQueryable()
                    join se in _unitOfWork.Repository<WorkoutSessionExercise>().GetQueryable() on log.WorkoutSessionExerciseId equals se.Id
                    where sessionIds.Contains(se.WorkoutSessionId)
                    select new { se.WorkoutSessionId, log.WorkoutSessionExerciseId })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.WorkoutSessionId)
                .ToDictionary(g => g.Key, g => (g.Count(), g.Select(x => x.WorkoutSessionExerciseId).Distinct().Count()));

        var sessionsByDay = sessions
            .Select(s =>
            {
                var (sets, exercises) = setCounts.GetValueOrDefault(s.Id);
                return new
                {
                    Day = DateOnly.FromDateTime(NotificationClock.ToLocal(s.CompletedAt, zone)),
                    Dto = new CustomerCalendarWorkoutSessionDto
                    {
                        Id = s.Id,
                        Title = workoutTitles.GetValueOrDefault(s.WorkoutId, string.Empty),
                        CompletedAt = DateTime.SpecifyKind(s.CompletedAt, DateTimeKind.Utc),
                        DurationSeconds = s.DurationSeconds,
                        CaloriesBurned = s.CaloriesBurned is null ? null : (int)Math.Round(s.CaloriesBurned.Value),
                        SetCount = sets,
                        ExerciseCount = exercises,
                    },
                };
            })
            .ToLookup(x => x.Day, x => x.Dto);

        // ---- Beslenme (Meal.Date yerel gunun UTC gece yarisi olarak saklanir) ----
        var mealStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var mealEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var mealItems = await (
                from item in _unitOfWork.Repository<MealItem>().GetQueryable()
                join meal in _unitOfWork.Repository<Meal>().GetQueryable() on item.MealId equals meal.Id
                where meal.UserId == userId && meal.Date >= mealStart && meal.Date < mealEnd
                select new { meal.Date, meal.MealType, item.Calories, item.Protein, item.Carbs, item.Fat })
            .ToListAsync(cancellationToken);
        var mealsByDay = mealItems.ToLookup(m => DateOnly.FromDateTime(m.Date));

        var goal = await _unitOfWork.Repository<NutritionGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        int? calorieTarget = goal is not null && goal.DailyCalories > 0 ? (int)Math.Round(goal.DailyCalories) : null;

        // ---- Program gunleri ----
        HashSet<DayOfWeek>? plannedDays = null;
        DateOnly? planStart = null;
        var plan = await _unitOfWork.Repository<WorkoutPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (plan is not null)
        {
            var days = await _unitOfWork.Repository<WorkoutPlanDay>()
                .GetQueryable()
                .Where(d => d.WorkoutPlanId == plan.Id)
                .Select(d => d.DayOfWeek)
                .ToListAsync(cancellationToken);
            if (days.Count > 0)
            {
                plannedDays = days.ToHashSet();
                planStart = DateOnly.FromDateTime(plan.StartDate);
            }
        }

        var result = new List<CustomerCalendarDayDto>(to.DayNumber - from.DayNumber + 1);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var isFuture = date > today;
            var daySessions = isFuture ? new List<CustomerCalendarWorkoutSessionDto>() : sessionsByDay[date].OrderBy(s => s.CompletedAt).ToList();
            var dayMeals = isFuture ? new List<(MealType MealType, decimal Calories, decimal Protein, decimal Carbs, decimal Fat)>()
                : mealsByDay[date].Select(m => (m.MealType, m.Calories, m.Protein, m.Carbs, m.Fat)).ToList();

            var consumed = (int)Math.Round(dayMeals.Sum(m => m.Calories));
            double? ratio = calorieTarget is > 0 && dayMeals.Count > 0 ? (double)consumed / calorieTarget.Value : null;
            var totalDuration = daySessions.Sum(s => s.DurationSeconds ?? 0);

            result.Add(new CustomerCalendarDayDto
            {
                Date = date.ToString("yyyy-MM-dd"),
                IsToday = date == today,
                IsFuture = isFuture,
                IsPlannedWorkoutDay = plannedDays is null || (planStart.HasValue && date < planStart.Value)
                    ? null
                    : plannedDays.Contains(date.DayOfWeek),
                Workout = new CustomerCalendarWorkoutDto
                {
                    Completed = daySessions.Count > 0,
                    TotalDurationSeconds = totalDuration,
                    Sessions = daySessions,
                },
                Nutrition = new CustomerCalendarNutritionDto
                {
                    Logged = dayMeals.Count > 0,
                    ConsumedCalories = consumed,
                    Protein = (int)Math.Round(dayMeals.Sum(m => m.Protein)),
                    Carbs = (int)Math.Round(dayMeals.Sum(m => m.Carbs)),
                    Fat = (int)Math.Round(dayMeals.Sum(m => m.Fat)),
                    CalorieRatio = ratio is null ? null : Math.Round(ratio.Value, 3),
                    CalorieGoalMet = ratio is >= NutritionInsightEvaluator.CompletedLower and <= NutritionInsightEvaluator.CompletedUpper,
                    Meals = dayMeals
                        .GroupBy(m => m.MealType)
                        .OrderBy(g => g.Key)
                        .Select(g => new CustomerCalendarMealDto
                        {
                            Type = MealLogWriter.MapMealType(g.Key),
                            Calories = (int)Math.Round(g.Sum(m => m.Calories)),
                            ItemCount = g.Count(),
                        })
                        .ToList(),
                },
                Activity = new CustomerCalendarActivityDto
                {
                    CaloriesBurned = daySessions.Sum(s => s.CaloriesBurned ?? 0),
                    ActiveMinutes = (int)Math.Round(totalDuration / 60d),
                },
            });
        }

        return new CustomerActivityCalendarResponse
        {
            From = from.ToString("yyyy-MM-dd"),
            To = to.ToString("yyyy-MM-dd"),
            Today = today.ToString("yyyy-MM-dd"),
            TimeZone = zone.Id,
            DailyCalorieTarget = calorieTarget,
            CalorieGoalLowerRatio = NutritionInsightEvaluator.CompletedLower,
            CalorieGoalUpperRatio = NutritionInsightEvaluator.CompletedUpper,
            Days = result,
        };
    }
}
