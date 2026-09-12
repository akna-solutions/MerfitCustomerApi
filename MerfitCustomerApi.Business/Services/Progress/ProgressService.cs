using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Progress;

/// <summary>
/// IProgressService'in varsayilan implementasyonu. Mevcut UserProfile/UserGoal/BodyMeasurement/
/// WorkoutSession/UserStreak/MerfitScore/MerfitScoreHistory/LeaderboardEntry entity'lerini
/// oldugu gibi kullanir, yeni entity eklemez.
/// </summary>
public class ProgressService : IProgressService
{
    private static readonly string[] TurkishMonthAbbreviations =
    {
        "Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara",
    };

    private readonly IUnitOfWork _unitOfWork;

    public ProgressService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerProgressResponse> GetProgressAsync(long userId, string range, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);

        var measurements = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.RecordedAt)
            .ToListAsync(cancellationToken);

        var latestMeasurement = measurements.LastOrDefault();
        var currentWeight = latestMeasurement?.WeightKg ?? profile.WeightKg ?? 0;
        var startingWeight = activeGoal?.StartingWeightKg ?? measurements.FirstOrDefault()?.WeightKg ?? currentWeight;
        var targetWeight = activeGoal?.TargetWeightKg ?? currentWeight;

        var now = DateTime.UtcNow;
        var monthAgoMeasurement = measurements
            .Where(m => m.RecordedAt <= now.AddDays(-30))
            .OrderByDescending(m => m.RecordedAt)
            .FirstOrDefault();
        var monthlyChange = monthAgoMeasurement is not null
            ? Math.Round(currentWeight - (monthAgoMeasurement.WeightKg ?? currentWeight), 1)
            : 0m;

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var completedSessions = await _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .Where(s => s.UserId == userId && s.Status == WorkoutSessionStatus.Completed)
            .ToListAsync(cancellationToken);

        var sessionsThisMonth = completedSessions.Where(s => s.CompletedAt >= monthStart).ToList();
        var workoutsThisMonth = sessionsThisMonth.Count;
        var caloriesThisMonth = (int)Math.Round(sessionsThisMonth.Sum(s => s.CaloriesBurned ?? 0));
        var trainingMinutesThisMonth = (int)Math.Round(sessionsThisMonth.Sum(s => (s.DurationSeconds ?? 0) / 60m));

        var streak = await _unitOfWork.Repository<UserStreak>()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var weekStart = now.Date.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
        var weeklyWorkouts = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = weekStart.AddDays(offset);
                return completedSessions.Any(s => s.CompletedAt.HasValue && s.CompletedAt.Value.Date == day);
            })
            .ToList();
        var workoutsThisWeek = weeklyWorkouts.Count(w => w);

        var rangeCutoff = range switch
        {
            "week" => now.AddDays(-7),
            "month" => now.AddDays(-30),
            "year" => now.AddDays(-365),
            _ => now.AddDays(-90), // "3months" ve taninmayan degerler icin varsayilan
        };
        var weightHistory = measurements
            .Where(m => m.RecordedAt >= rangeCutoff)
            .Select(m => new CustomerWeightPointDto
            {
                Date = FormatShortDate(m.RecordedAt),
                Weight = m.WeightKg ?? 0,
            })
            .ToList();

        var bodyMetrics = new List<CustomerBodyMetricDto>();
        if (latestMeasurement?.WeightKg is not null)
        {
            bodyMetrics.Add(new CustomerBodyMetricDto { Id = "weight", Label = "Kilo", Value = $"{latestMeasurement.WeightKg:0.0} kg" });
        }
        if (latestMeasurement?.BodyFatPercentage is not null)
        {
            bodyMetrics.Add(new CustomerBodyMetricDto { Id = "body-fat", Label = "Vücut Yağı", Value = $"%{latestMeasurement.BodyFatPercentage:0.0}" });
        }

        var recentSessions = completedSessions
            .Where(s => s.CompletedAt.HasValue)
            .OrderByDescending(s => s.CompletedAt)
            .Take(5)
            .ToList();
        var workoutIds = recentSessions.Select(s => s.WorkoutId).Distinct().ToList();
        var workoutTitles = workoutIds.Count == 0
            ? new Dictionary<long, string>()
            : await _unitOfWork.Repository<Workout>()
                .GetQueryable()
                .Where(w => workoutIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Title, cancellationToken);

        var recentActivity = recentSessions.Select(s => new CustomerRecentActivityDto
        {
            Id = s.Id,
            Title = workoutTitles.GetValueOrDefault(s.WorkoutId, string.Empty),
            DurationMin = (int)Math.Round((s.DurationSeconds ?? 0) / 60m),
            DateLabel = FormatRelativeDateLabel(s.CompletedAt!.Value, now),
        }).ToList();

        var score = await BuildScoreSummaryAsync(userId, cancellationToken);

        var goalType = activeGoal?.GoalType ?? profile.Goal ?? FitnessGoal.MaintainWeight;

        return new CustomerProgressResponse
        {
            HasCompletedFirstWorkout = completedSessions.Count > 0,
            GoalType = MapGoalType(goalType),
            GoalLabel = MapGoalLabel(goalType),
            GoalPercent = IsPercentGoal(goalType) ? (activeGoal?.ProgressPercent ?? 0) : null,
            WorkoutsCompleted = goalType == FitnessGoal.ImproveFitness ? workoutsThisWeek : null,
            WorkoutsGoal = goalType == FitnessGoal.ImproveFitness ? (profile.TrainingDaysPerWeek ?? 3) : null,
            CurrentWeight = currentWeight,
            StartingWeight = startingWeight,
            TargetWeight = targetWeight,
            MonthlyChange = monthlyChange,
            Workouts = workoutsThisMonth,
            Calories = caloriesThisMonth,
            Streak = streak?.CurrentStreak ?? 0,
            TrainingMinutes = trainingMinutesThisMonth,
            WeeklyWorkouts = weeklyWorkouts,
            WeightHistory = weightHistory,
            BodyMetrics = bodyMetrics,
            RecentActivity = recentActivity,
            Score = score,
        };
    }

    private async Task<CustomerScoreSummaryDto?> BuildScoreSummaryAsync(long userId, CancellationToken cancellationToken)
    {
        var latestScore = await _unitOfWork.Repository<MerfitScore>()
            .GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestScore is null)
        {
            return null;
        }

        var weekAgo = latestScore.CalculatedAt.AddDays(-7);
        var pastScore = await _unitOfWork.Repository<MerfitScoreHistory>()
            .GetQueryable()
            .Where(h => h.UserId == userId && h.RecordedAt <= weekAgo)
            .OrderByDescending(h => h.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var weeklyChange = pastScore is not null
            ? (int)Math.Round(latestScore.Score - pastScore.Score)
            : 0;

        var activeEntry = await (
            from entry in _unitOfWork.Repository<LeaderboardEntry>().GetQueryable()
            join period in _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable()
                on entry.LeaderboardPeriodId equals period.Id
            where entry.UserId == userId && period.IsActive
            select (int?)entry.Rank
        ).FirstOrDefaultAsync(cancellationToken);

        return new CustomerScoreSummaryDto
        {
            Points = (int)Math.Round(latestScore.Score),
            WeeklyChange = weeklyChange,
            Rank = activeEntry,
        };
    }

    private static bool IsPercentGoal(FitnessGoal goal) =>
        goal is FitnessGoal.BuildMuscle or FitnessGoal.GetStronger or FitnessGoal.ImproveEndurance;

    private static string MapGoalType(FitnessGoal goal) => goal switch
    {
        FitnessGoal.LoseWeight => "lose_weight",
        FitnessGoal.BuildMuscle => "build_muscle",
        FitnessGoal.GetStronger => "get_stronger",
        FitnessGoal.ImproveFitness => "improve_fitness",
        FitnessGoal.MaintainWeight => "maintain_weight",
        FitnessGoal.ImproveEndurance => "improve_endurance",
        _ => "maintain_weight",
    };

    private static string MapGoalLabel(FitnessGoal goal) => goal switch
    {
        FitnessGoal.LoseWeight => "Kilo vermek",
        FitnessGoal.BuildMuscle => "Kas yapmak",
        FitnessGoal.GetStronger => "Daha güçlü olmak",
        FitnessGoal.ImproveFitness => "Fitness seviyesini artırmak",
        FitnessGoal.MaintainWeight => "Kilomu korumak",
        FitnessGoal.ImproveEndurance => "Dayanıklılığı artırmak",
        _ => "Kilomu korumak",
    };

    private static string FormatShortDate(DateTime date) => $"{TurkishMonthAbbreviations[date.Month - 1]} {date.Day:00}";

    private static string FormatRelativeDateLabel(DateTime date, DateTime now)
    {
        var days = (now.Date - date.Date).Days;
        return days switch
        {
            0 => "Bugün",
            1 => "Dün",
            _ => FormatShortDate(date),
        };
    }
}
