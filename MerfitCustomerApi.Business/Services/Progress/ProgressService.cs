using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

    private const int MaxBackfillDays = 365;

    private readonly IUnitOfWork _unitOfWork;

    private readonly IMerfitScoreService _scoreService;

    private readonly BodyCompositionOptions _bodyCompositionOptions;

    public ProgressService(IUnitOfWork unitOfWork, IMerfitScoreService scoreService, IOptions<BodyCompositionOptions>? bodyCompositionOptions = null)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
        _bodyCompositionOptions = bodyCompositionOptions?.Value ?? new BodyCompositionOptions();
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

        var allMeasurements = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.RecordedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        // Ayni gun hem gunluk kilo hem tam vucut olcumu kaydi olabilir: kilo serisinde gun basina en son kayit.
        var measurements = allMeasurements
            .Where(m => m.WeightKg is not null)
            .GroupBy(m => m.RecordedAt.Date)
            .Select(g => g.Last())
            .ToList();
        var latestMeasurement = measurements.LastOrDefault();
        var currentWeight = latestMeasurement?.WeightKg ?? profile.WeightKg ?? 0;

        // Eski kayitlarda hedef kilo bos olabilir: hedefe gore otomatik hesaplanip kaydedilir.
        if (activeGoal is not null && WeightTracking.EnsureWeightGoal(activeGoal, profile, latestMeasurement?.WeightKg))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var startingWeight = activeGoal?.StartingWeightKg ?? measurements.FirstOrDefault()?.WeightKg ?? currentWeight;
        var targetWeight = activeGoal?.TargetWeightKg ?? profile.TargetWeightKg ?? currentWeight;

        var now = DateTime.UtcNow;
        var monthlyChange = CalculateMonthlyChange(measurements, currentWeight, now);
        var today = DateOnly.FromDateTime(now);
        var todayMeasurement = latestMeasurement is not null && DateOnly.FromDateTime(latestMeasurement.RecordedAt) == today
            ? latestMeasurement
            : null;

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
                IsoDate = m.RecordedAt.ToString("yyyy-MM-dd"),
                Weight = m.WeightKg ?? 0,
            })
            .ToList();

        // Yag orani serisi: girilen/olculen deger, yoksa cevre olcumunden tahmin (isaretlenerek).
        var bodyFatHistory = allMeasurements
            .Where(m => m.RecordedAt >= rangeCutoff && (m.BodyFatPercentage ?? m.EstimatedBodyFatPercentage) is not null)
            .Select(m => new CustomerBodyMetricPointDto
            {
                Date = FormatShortDate(m.RecordedAt),
                IsoDate = m.RecordedAt.ToString("yyyy-MM-dd"),
                Value = (m.BodyFatPercentage ?? m.EstimatedBodyFatPercentage)!.Value,
                IsEstimated = m.BodyFatPercentage is null,
            })
            .ToList();
        var waistHistory = allMeasurements
            .Where(m => m.RecordedAt >= rangeCutoff && m.WaistCm is not null)
            .Select(m => new CustomerBodyMetricPointDto
            {
                Date = FormatShortDate(m.RecordedAt),
                IsoDate = m.RecordedAt.ToString("yyyy-MM-dd"),
                Value = m.WaistCm!.Value,
            })
            .ToList();

        var bodyMetrics = new List<CustomerBodyMetricDto>();
        if (latestMeasurement?.WeightKg is not null)
        {
            bodyMetrics.Add(new CustomerBodyMetricDto { Id = "weight", Label = "Kilo", Value = $"{latestMeasurement.WeightKg:0.0} kg" });
        }

        var bodyFat = BodyFatAssessor.Assess(profile, allMeasurements, now, _bodyCompositionOptions);
        if (bodyFat.Reading is not null)
        {
            bodyMetrics.Add(bodyFat.Reading.Source == BodyFatSource.Estimated
                ? new CustomerBodyMetricDto { Id = "body-fat", Label = "Vücut Yağı (tahmini)", Value = $"~%{bodyFat.Reading.Percentage:0.0}" }
                : new CustomerBodyMetricDto { Id = "body-fat", Label = "Vücut Yağı", Value = $"%{bodyFat.Reading.Percentage:0.0}" });
        }

        var latestWaist = allMeasurements.LastOrDefault(m => m.WaistCm is not null);
        if (latestWaist is not null)
        {
            bodyMetrics.Add(new CustomerBodyMetricDto { Id = "waist", Label = "Bel", Value = $"{latestWaist.WaistCm:0.#} cm" });
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

        var goalType = FitnessGoals.Normalize(activeGoal?.GoalType ?? profile.Goal);

        return new CustomerProgressResponse
        {
            HasCompletedFirstWorkout = completedSessions.Count > 0,
            GoalType = MapGoalType(goalType),
            GoalLabel = MapGoalLabel(goalType),
            // Uc ana hedefin hepsinde baslangictan hedef kiloya kat edilen yol.
            GoalPercent = currentWeight <= 0
                ? null
                : WeightTracking.CalculateProgressPercent(startingWeight, currentWeight, targetWeight),
            WeightDirection = WeightTracking.Direction(startingWeight, targetWeight),
            WorkoutsCompleted = null,
            WorkoutsGoal = null,
            CurrentWeight = currentWeight,
            StartingWeight = startingWeight,
            TargetWeight = targetWeight,
            TodayWeight = todayMeasurement?.WeightKg,
            MonthlyChange = monthlyChange,
            Workouts = workoutsThisMonth,
            Calories = caloriesThisMonth,
            Streak = streak?.CurrentStreak ?? 0,
            TrainingMinutes = trainingMinutesThisMonth,
            WeeklyWorkouts = weeklyWorkouts,
            WeightHistory = weightHistory,
            BodyFatHistory = bodyFatHistory,
            WaistHistory = waistHistory,
            BodyMetrics = bodyMetrics,
            RecentActivity = recentActivity,
            Score = score,
        };
    }

    /// <summary>
    /// Tum zamanlar MB FIT puani ve sirasi (LeaderboardScreen'deki "Tüm Zamanlar" ile ayni hesap).
    /// Hic puani olmayan kullanici icin de 0 puanla doner ki ScoreCard ve siralamaya giris gorunsun.
    /// </summary>
    private async Task<CustomerScoreSummaryDto> BuildScoreSummaryAsync(long userId, CancellationToken cancellationToken)
    {
        var ranked = await _scoreService.RankUsersAsync(null, null, cancellationToken);
        var current = ranked.FirstOrDefault(r => r.UserId == userId);

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var weeklyEvents = await _scoreService.GetPointEventsAsync(weekAgo, null, userId, cancellationToken);

        return new CustomerScoreSummaryDto
        {
            Points = current?.Points ?? 0,
            WeeklyChange = weeklyEvents.Sum(e => e.Points),
            Rank = current?.Rank,
        };
    }

    public async Task<CustomerWeightLogResponse> LogWeightAsync(long userId, LogWeightRequest request, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var date = request.Date ?? today;
        // Kullanicinin saat dilimi UTC'nin onunde olabilir; yarina kadar kabul edilir.
        if (date > today.AddDays(1))
        {
            throw new AppValidationException(nameof(request.Date), "Gelecek bir tarihe kilo girilemez.");
        }

        if (date < today.AddDays(-MaxBackfillDays))
        {
            throw new AppValidationException(nameof(request.Date), "En fazla bir yil oncesine kilo girilebilir.");
        }

        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(UserProfile), userId);

        var weight = Math.Round(request.WeightKg, 1);
        await WeightTracking.UpsertDailyWeightAsync(_unitOfWork, userId, date, weight, profile.HeightCm, cancellationToken);

        // Gecmise donuk bir giris guncel kiloyu degistirmez; yalnizca en yeni kayit "guncel" kilodur.
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hasNewerMeasurement = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .AnyAsync(m => m.UserId == userId && m.RecordedAt > dayStart && m.WeightKg != null, cancellationToken);

        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);

        if (!hasNewerMeasurement)
        {
            await WeightTracking.ApplyNewestWeightAsync(_unitOfWork, profile, activeGoal, weight, cancellationToken);
        }

        if (activeGoal is not null)
        {
            WeightTracking.EnsureWeightGoal(activeGoal, profile, weight);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        return new CustomerWeightLogResponse
        {
            Date = date.ToString("yyyy-MM-dd"),
            WeightKg = weight,
            TargetWeight = activeGoal?.TargetWeightKg ?? profile.TargetWeightKg,
        };
    }

    /// <summary>
    /// Son 30 gundeki degisim: 30 gun once veya daha eski en yeni kayit baz alinir; yoksa son 30 gunun ilk kaydi.
    /// </summary>
    private static decimal CalculateMonthlyChange(List<BodyMeasurement> measurements, decimal currentWeight, DateTime now)
    {
        var cutoff = now.AddDays(-30);
        var baseline = measurements.LastOrDefault(m => m.RecordedAt <= cutoff)
            ?? measurements.FirstOrDefault(m => m.RecordedAt > cutoff);
        if (baseline is null || baseline == measurements.LastOrDefault())
        {
            return 0m;
        }

        return Math.Round(currentWeight - baseline.WeightKg!.Value, 1);
    }

    /// <summary>Eski hedefler ana hedeflere eslenir; yanit yalnizca uc ana hedef kodunu icerir.</summary>
    private static string MapGoalType(FitnessGoal goal) => FitnessGoals.Normalize(goal) switch
    {
        FitnessGoal.LoseWeight => "lose_weight",
        FitnessGoal.GetStronger => "get_stronger",
        _ => "build_muscle",
    };

    private static string MapGoalLabel(FitnessGoal goal) => FitnessGoals.Label(goal);

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
