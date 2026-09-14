using MerfitCustomerApi.Business.Dtos.Customer.WorkoutPlans;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Personalization;

/// <summary>
/// IMyPlanService'in varsayilan implementasyonu. Yalnizca okuma yapar (generation yok);
/// N+1 sorgu olusturmamak icin gunler/egzersizler/workout'lar toplu (batched) sorgularla
/// cekilir ve bellekte eslenir. Tum sorgular AsNoTracking (GetQueryable varsayilani) kullanir.
/// </summary>
public class MyPlanService : IMyPlanService
{
    private readonly IUnitOfWork _unitOfWork;

    public MyPlanService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerWorkoutPlanDto?> GetActivePlanAsync(long userId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<WorkoutPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var days = await _unitOfWork.Repository<WorkoutPlanDay>()
            .GetQueryable()
            .Where(d => d.WorkoutPlanId == plan.Id)
            .OrderBy(d => d.DayOfWeek)
            .ToListAsync(cancellationToken);

        var dayDtos = await BuildDayDtosAsync(days, cancellationToken);

        return new CustomerWorkoutPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Goal = plan.Goal?.ToString(),
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            IsActive = plan.IsActive,
            // WorkoutPlanGenerator, kural-tabanli aciklamayi (Reason/Summary) Description alanina yazar.
            Summary = plan.Description,
            Days = dayDtos,
        };
    }

    public async Task<CustomerTodayWorkoutPlanResponse> GetTodayAsync(long userId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<WorkoutPlan>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        if (plan is null)
        {
            return new CustomerTodayWorkoutPlanResponse { HasActivePlan = false, HasWorkoutToday = false };
        }

        var today = DateTime.UtcNow.DayOfWeek;
        var day = await _unitOfWork.Repository<WorkoutPlanDay>()
            .FirstOrDefaultAsync(d => d.WorkoutPlanId == plan.Id && d.DayOfWeek == today, cancellationToken);
        if (day is null)
        {
            return new CustomerTodayWorkoutPlanResponse { HasActivePlan = true, HasWorkoutToday = false };
        }

        var dayDtos = await BuildDayDtosAsync(new List<WorkoutPlanDay> { day }, cancellationToken);

        return new CustomerTodayWorkoutPlanResponse
        {
            HasActivePlan = true,
            HasWorkoutToday = true,
            Day = dayDtos.FirstOrDefault(),
            WorkoutPlanDayId = day.Id,
        };
    }

    /// <summary>
    /// Verilen WorkoutPlanDay listesi icin Workout/WorkoutPlanExercise/Exercise verilerini
    /// TOPLU sorgularla ceker ve DTO'lara esler (N+1 onlenir).
    /// </summary>
    private async Task<List<CustomerWorkoutPlanDayDto>> BuildDayDtosAsync(
        List<WorkoutPlanDay> days,
        CancellationToken cancellationToken)
    {
        if (days.Count == 0)
        {
            return new List<CustomerWorkoutPlanDayDto>();
        }

        var dayIds = days.Select(d => d.Id).ToList();
        var workoutIds = days.Select(d => d.WorkoutId).Distinct().ToList();

        var workouts = await _unitOfWork.Repository<Workout>()
            .GetQueryable()
            .Where(w => workoutIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        var planExercises = await _unitOfWork.Repository<WorkoutPlanExercise>()
            .GetQueryable()
            .Where(pe => dayIds.Contains(pe.WorkoutPlanDayId))
            .OrderBy(pe => pe.Order)
            .ToListAsync(cancellationToken);

        var exerciseIds = planExercises.Select(pe => pe.ExerciseId).Distinct().ToList();
        var exercises = await _unitOfWork.Repository<Exercise>()
            .GetQueryable()
            .Where(e => exerciseIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        return days.Select(day =>
        {
            var workout = workouts.GetValueOrDefault(day.WorkoutId);
            var exercisesForDay = planExercises.Where(pe => pe.WorkoutPlanDayId == day.Id);

            return new CustomerWorkoutPlanDayDto
            {
                DayOfWeek = day.DayOfWeek.ToString(),
                Workout = new CustomerWorkoutPlanWorkoutDto
                {
                    Id = day.WorkoutId,
                    Title = workout?.Title ?? string.Empty,
                    DurationMin = workout?.DurationMin ?? 0,
                    ImageUrl = workout?.ImageUrl,
                },
                Exercises = exercisesForDay.Select(pe => new CustomerWorkoutPlanExerciseDto
                {
                    ExerciseId = pe.ExerciseId,
                    Name = exercises.GetValueOrDefault(pe.ExerciseId)?.Name ?? string.Empty,
                    Order = pe.Order,
                    Sets = pe.Sets,
                    Reps = pe.Reps,
                    RestSeconds = pe.RestSeconds,
                    DurationSeconds = pe.DurationSeconds,
                }).ToList(),
            };
        }).ToList();
    }
}
