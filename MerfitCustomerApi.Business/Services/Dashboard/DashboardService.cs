using MerfitCustomerApi.Business.Dtos.Customer.Dashboard;
using MerfitCustomerApi.Business.Dtos.Customer.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Business.Services.Workouts;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Dashboard;

/// <summary>
/// IDashboardService'in varsayilan implementasyonu. Mevcut UserProfile/UserStreak/UserGoal/
/// WorkoutSession entity'lerinden ve IWorkoutService'in kisisellestirme mantigindan yararlanarak
/// MerfitNativeApp DashboardScreen'in tek cagriyla ihtiyac duydugu tum veriyi derler.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkoutService _workoutService;

    public DashboardService(IUnitOfWork unitOfWork, IWorkoutService workoutService)
    {
        _unitOfWork = unitOfWork;
        _workoutService = workoutService;
    }

    public async Task<CustomerDashboardResponse> GetDashboardAsync(long userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var today = DateTime.UtcNow.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));

        var completedSessions = await _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .Where(s => s.UserId == userId && s.Status == WorkoutSessionStatus.Completed)
            .ToListAsync(cancellationToken);

        var hasCompletedFirstWorkout = completedSessions.Count > 0;
        // Haftalik hedef "haftada X gun" oldugu icin ayni gun yapilan birden fazla antrenman tek gun sayilir.
        var workoutsCompletedThisWeek = completedSessions
            .Where(s => s.CompletedAt.HasValue && s.CompletedAt.Value.Date >= weekStart)
            .Select(s => s.CompletedAt!.Value.Date)
            .Distinct()
            .Count();
        var caloriesToday = completedSessions
            .Where(s => s.CompletedAt.HasValue && s.CompletedAt.Value.Date == today)
            .Sum(s => s.CaloriesBurned ?? 0);

        var weeklyTarget = await GetWeeklyWorkoutTargetAsync(userId, profile, cancellationToken);

        var streak = await _unitOfWork.Repository<UserStreak>()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);

        var latestMeasurement = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var currentWeightKg = latestMeasurement?.WeightKg ?? profile.WeightKg;

        // Eski kayitlarda hedef kilo bos olabilir: hedefe gore otomatik hesaplanip kaydedilir.
        if (activeGoal is not null && WeightTracking.EnsureWeightGoal(activeGoal, profile, latestMeasurement?.WeightKg))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var recommendedPage = await _workoutService.GetWorkoutsAsync(
            userId,
            new CustomerWorkoutListRequest { Personalized = true, Page = 1, PageSize = 5 },
            cancellationToken);

        CustomerDashboardWorkoutDto? todayWorkout = null;
        var recommended = new List<CustomerDashboardWorkoutDto>();

        var hasWorkoutToday = completedSessions.Any(s => s.CompletedAt.HasValue && s.CompletedAt.Value.Date == today);
        for (var i = 0; i < recommendedPage.Items.Count; i++)
        {
            var summary = MapToDashboardWorkout(recommendedPage.Items[i]);
            if (i == 0 && !hasWorkoutToday)
            {
                todayWorkout = summary;
            }
            else
            {
                recommended.Add(summary);
            }
        }

        var quickStats = new List<CustomerQuickStatDto>
        {
            new()
            {
                Id = "weight",
                Label = "Kilo",
                Value = currentWeightKg.HasValue ? $"{currentWeightKg.Value:0.0} kg" : "—",
                Icon = "scale",
            },
            new()
            {
                Id = "streak",
                Label = "Seri",
                Value = $"{streak?.CurrentStreak ?? 0} gün",
                Icon = "flame",
            },
            new()
            {
                Id = "calories",
                Label = "Kalori",
                Value = $"{caloriesToday:0} kcal",
                Icon = "trophy",
            },
            new()
            {
                Id = "workouts",
                Label = "Bu hafta",
                Value = $"{workoutsCompletedThisWeek} antrenman",
                Icon = "barbell",
            },
        };

        return new CustomerDashboardResponse
        {
            UserName = profile.FirstName,
            HasCompletedFirstWorkout = hasCompletedFirstWorkout,
            TodayProgress = new CustomerTodayProgressDto
            {
                WorkoutsCompleted = workoutsCompletedThisWeek,
                WorkoutsTarget = weeklyTarget,
                Calories = (int)caloriesToday,
            },
            TodayWorkout = todayWorkout,
            QuickStats = quickStats,
            GoalProgress = activeGoal is null || activeGoal.TargetWeightKg is null || activeGoal.StartingWeightKg is null
                ? null
                : new CustomerGoalProgressDto
                {
                    CurrentWeightKg = currentWeightKg ?? activeGoal.CurrentWeightKg ?? activeGoal.StartingWeightKg.Value,
                    GoalWeightKg = activeGoal.TargetWeightKg.Value,
                    StartWeightKg = activeGoal.StartingWeightKg.Value,
                },
            Recommended = recommended,
        };
    }

    /// <summary>
    /// Haftalik antrenman hedefi: aktif kisisel planda kac antrenman gunu varsa o (kullaniciya gercekte
    /// verilen program), plan yoksa profildeki haftalik gun sayisi, o da yoksa 3.
    /// </summary>
    private async Task<int> GetWeeklyWorkoutTargetAsync(long userId, UserProfile profile, CancellationToken cancellationToken)
    {
        var activePlan = await _unitOfWork.Repository<WorkoutPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (activePlan is not null)
        {
            var planDayCount = await _unitOfWork.Repository<WorkoutPlanDay>()
                .GetQueryable()
                .Where(d => d.WorkoutPlanId == activePlan.Id)
                .Select(d => d.DayOfWeek)
                .Distinct()
                .CountAsync(cancellationToken);
            if (planDayCount > 0)
            {
                return planDayCount;
            }
        }

        return profile.TrainingDaysPerWeek ?? 3;
    }

    private static CustomerDashboardWorkoutDto MapToDashboardWorkout(CustomerWorkoutListItemDto workout) => new()
    {
        Id = workout.Id,
        Title = workout.Title,
        Duration = $"{workout.DurationMin} dk",
        Meta = workout.Tagline ?? workout.Category,
        Difficulty = workout.Difficulty,
        ImageUrl = workout.ImageUrl,
    };
}
