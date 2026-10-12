using System.Globalization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Scoring;

/// <summary>
/// IMerfitScoreService'in varsayilan implementasyonu.
///
/// Puanlar ayri bir defterde (ledger) tutulmaz; kaynak tablolardan turetilir:
/// - workouts: tamamlanan WorkoutSession (gunde en fazla MaxScoredWorkoutsPerDay oturum puan alir)
/// - personal_records: PersonalRecord kayitlari
/// - nutrition: ogun kalemi (MealItem) girilen her gun, kaydin girildigi gune gore (geriye donuk tarihli
///   kayitla puan kasilmasin diye Meal.Date degil MealItem.CreatedAt kullanilir)
/// - water: su kaydi (WaterLog) girilen her gun, yine CreatedAt'e gore
/// - achievements: kazanilan basarinin Achievement.Points degeri, UserAchievement.EarnedAt anina yazilir
/// Boylece sema degisikligi gerekmez, mevcut kullanicilarin gecmis aktiviteleri de puana yansir ve
/// silinen bir kayit puani da geri alir.
/// </summary>
public class MerfitScoreService : IMerfitScoreService
{
    public const string AllTimePeriodTag = "AllTime";

    public const string WorkoutsCategory = "workouts";
    public const string PersonalRecordsCategory = "personal_records";
    public const string NutritionCategory = "nutrition";
    public const string WaterCategory = "water";
    public const string AchievementsCategory = "achievements";

    /// <summary>Admin panelinde Achievement.ConditionType icin desteklenen degerler.</summary>
    public const string WorkoutsCompletedCondition = "workouts_completed";
    public const string StreakDaysCondition = "streak_days";
    public const string PersonalRecordsCondition = "personal_records";
    public const string MealLogDaysCondition = "meal_log_days";
    public const string WaterLogDaysCondition = "water_log_days";
    public const string TotalPointsCondition = "total_points";

    private readonly IUnitOfWork _unitOfWork;
    private readonly MerfitScoringOptions _options;
    private readonly ILogger<MerfitScoreService> _logger;

    public MerfitScoreService(IUnitOfWork unitOfWork, IOptions<MerfitScoringOptions> options, ILogger<MerfitScoreService> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    private TimeSpan Offset => TimeSpan.FromHours(_options.TimeZoneOffsetHours);

    public DateTime GetLocalDayStartUtc(DateTime utc) => DateTime.SpecifyKind((utc + Offset).Date - Offset, DateTimeKind.Utc);

    public async Task<IReadOnlyList<MerfitPointEvent>> GetPointEventsAsync(DateTime? fromUtc, DateTime? toUtc, long? userId = null, CancellationToken cancellationToken = default)
    {
        var events = new List<MerfitPointEvent>();

        // Antrenmanlar: gunluk ust sinir yerel gune gore uygulanir.
        var sessionQuery = _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.Status == WorkoutSessionStatus.Completed && s.CompletedAt != null);
        if (userId.HasValue) sessionQuery = sessionQuery.Where(s => s.UserId == userId.Value);
        if (fromUtc.HasValue) sessionQuery = sessionQuery.Where(s => s.CompletedAt >= fromUtc.Value);
        if (toUtc.HasValue) sessionQuery = sessionQuery.Where(s => s.CompletedAt < toUtc.Value);
        var sessions = await sessionQuery
            .Select(s => new { s.UserId, CompletedAt = s.CompletedAt!.Value })
            .ToListAsync(cancellationToken);

        events.AddRange(sessions
            .GroupBy(s => new { s.UserId, Day = LocalDate(s.CompletedAt) })
            .SelectMany(g => g.OrderBy(s => s.CompletedAt).Take(Math.Max(0, _options.MaxScoredWorkoutsPerDay)))
            .Select(s => new MerfitPointEvent(s.UserId, s.CompletedAt, WorkoutsCategory, _options.WorkoutCompletedPoints)));

        // Kisisel rekorlar.
        var recordQuery = _unitOfWork.Repository<PersonalRecord>().GetQueryable();
        if (userId.HasValue) recordQuery = recordQuery.Where(r => r.UserId == userId.Value);
        if (fromUtc.HasValue) recordQuery = recordQuery.Where(r => r.AchievedAt >= fromUtc.Value);
        if (toUtc.HasValue) recordQuery = recordQuery.Where(r => r.AchievedAt < toUtc.Value);
        var records = await recordQuery.Select(r => new { r.UserId, r.AchievedAt }).ToListAsync(cancellationToken);
        events.AddRange(records.Select(r => new MerfitPointEvent(r.UserId, r.AchievedAt, PersonalRecordsCategory, _options.PersonalRecordPoints)));

        // Ogun kaydi: gunde bir kez.
        var mealQuery =
            from item in _unitOfWork.Repository<MealItem>().GetQueryable()
            join meal in _unitOfWork.Repository<Meal>().GetQueryable() on item.MealId equals meal.Id
            select new { meal.UserId, item.CreatedAt };
        if (userId.HasValue) mealQuery = mealQuery.Where(m => m.UserId == userId.Value);
        if (fromUtc.HasValue) mealQuery = mealQuery.Where(m => m.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) mealQuery = mealQuery.Where(m => m.CreatedAt < toUtc.Value);
        var mealLogs = await mealQuery.ToListAsync(cancellationToken);
        events.AddRange(FirstPerDay(mealLogs.Select(m => (m.UserId, m.CreatedAt)), NutritionCategory, _options.MealLogDayPoints));

        // Su kaydi: gunde bir kez.
        var waterQuery = _unitOfWork.Repository<WaterLog>().GetQueryable();
        if (userId.HasValue) waterQuery = waterQuery.Where(w => w.UserId == userId.Value);
        if (fromUtc.HasValue) waterQuery = waterQuery.Where(w => w.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) waterQuery = waterQuery.Where(w => w.CreatedAt < toUtc.Value);
        var waterLogs = await waterQuery.Select(w => new { w.UserId, w.CreatedAt }).ToListAsync(cancellationToken);
        events.AddRange(FirstPerDay(waterLogs.Select(w => (w.UserId, w.CreatedAt)), WaterCategory, _options.WaterLogDayPoints));

        // Basarilar.
        var achievementQuery =
            from ua in _unitOfWork.Repository<UserAchievement>().GetQueryable()
            join a in _unitOfWork.Repository<Achievement>().GetQueryable() on ua.AchievementId equals a.Id
            where a.Points > 0
            select new { ua.UserId, ua.EarnedAt, a.Points };
        if (userId.HasValue) achievementQuery = achievementQuery.Where(x => x.UserId == userId.Value);
        if (fromUtc.HasValue) achievementQuery = achievementQuery.Where(x => x.EarnedAt >= fromUtc.Value);
        if (toUtc.HasValue) achievementQuery = achievementQuery.Where(x => x.EarnedAt < toUtc.Value);
        var achievements = await achievementQuery.ToListAsync(cancellationToken);
        events.AddRange(achievements.Select(x => new MerfitPointEvent(x.UserId, x.EarnedAt, AchievementsCategory, x.Points)));

        return events.Where(e => e.Points > 0).ToList();
    }

    public async Task<List<MerfitRankedUser>> RankUsersAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var events = await GetPointEventsAsync(fromUtc, toUtc, null, cancellationToken);

        var workoutQuery = _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.Status == WorkoutSessionStatus.Completed && s.CompletedAt != null);
        if (fromUtc.HasValue) workoutQuery = workoutQuery.Where(s => s.CompletedAt >= fromUtc.Value);
        if (toUtc.HasValue) workoutQuery = workoutQuery.Where(s => s.CompletedAt < toUtc.Value);
        var workoutCounts = await workoutQuery
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        return events
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Points = g.Sum(e => e.Points), Workouts = workoutCounts.GetValueOrDefault(g.Key, 0) })
            .Where(x => x.Points > 0)
            .OrderByDescending(x => x.Points)
            .ThenByDescending(x => x.Workouts)
            .ThenBy(x => x.UserId)
            .Select((x, index) => new MerfitRankedUser(x.UserId, index + 1, x.Points, x.Workouts))
            .ToList();
    }

    public async Task<MerfitScoreWindow> ResolveWindowAsync(LeaderboardPeriodType periodType, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var period = await _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable()
            .Where(p => p.Type == periodType && p.IsActive && p.StartDate <= now && p.EndDate >= now)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (periodType == LeaderboardPeriodType.AllTime)
        {
            return new MerfitScoreWindow(null, null, period);
        }

        if (period is not null)
        {
            return new MerfitScoreWindow(period.StartDate, period.EndDate, period);
        }

        var localToday = (now + Offset).Date;
        var localStart = periodType == LeaderboardPeriodType.Weekly
            ? localToday.AddDays(-(((int)localToday.DayOfWeek + 6) % 7))
            : new DateTime(localToday.Year, localToday.Month, 1);
        var localEnd = periodType == LeaderboardPeriodType.Weekly ? localStart.AddDays(7) : localStart.AddMonths(1);

        return new MerfitScoreWindow(
            DateTime.SpecifyKind(localStart - Offset, DateTimeKind.Utc),
            DateTime.SpecifyKind(localEnd - Offset, DateTimeKind.Utc),
            null);
    }

    public async Task TryRefreshUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await AwardAchievementsAsync(userId, cancellationToken);
            await UpdateScoreSnapshotAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Puan ozeti bir sonraki aksiyonda yeniden hesaplanir; kullanicinin asil islemi bozulmamali.
            _unitOfWork.ClearTracking();
            _logger.LogWarning(ex, "MB FIT puan ozeti guncellenemedi. UserId={UserId}", userId);
        }
    }

    private async Task AwardAchievementsAsync(long userId, CancellationToken cancellationToken)
    {
        var earnedIds = await _unitOfWork.Repository<UserAchievement>().GetQueryable()
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync(cancellationToken);

        var candidates = await _unitOfWork.Repository<Achievement>().GetQueryable()
            .Where(a => a.IsActive && !earnedIds.Contains(a.Id))
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var metrics = new Dictionary<string, int>();
        var now = DateTime.UtcNow;
        var awarded = 0;

        foreach (var achievement in candidates)
        {
            var conditionType = achievement.ConditionType.Trim().ToLowerInvariant();
            if (!int.TryParse(achievement.ConditionValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold))
            {
                continue;
            }

            if (!metrics.TryGetValue(conditionType, out var value))
            {
                var metric = await GetMetricAsync(userId, conditionType, cancellationToken);
                if (metric is null)
                {
                    continue;
                }

                value = metric.Value;
                metrics[conditionType] = value;
            }

            if (value < threshold)
            {
                continue;
            }

            await _unitOfWork.Repository<UserAchievement>().AddAsync(new UserAchievement
            {
                UserId = userId,
                AchievementId = achievement.Id,
                EarnedAt = now,
                CreatedAt = now,
            }, cancellationToken);
            awarded++;
        }

        if (awarded > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Desteklenmeyen kosul turu icin null doner (basari atlanir).</summary>
    private async Task<int?> GetMetricAsync(long userId, string conditionType, CancellationToken cancellationToken)
    {
        switch (conditionType)
        {
            case WorkoutsCompletedCondition:
                return await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
                    .CountAsync(s => s.UserId == userId && s.Status == WorkoutSessionStatus.Completed, cancellationToken);
            case StreakDaysCondition:
                var streak = await _unitOfWork.Repository<UserStreak>().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
                return streak is null ? 0 : Math.Max(streak.CurrentStreak, streak.LongestStreak);
            case PersonalRecordsCondition:
                return await _unitOfWork.Repository<PersonalRecord>().GetQueryable()
                    .CountAsync(r => r.UserId == userId, cancellationToken);
            case MealLogDaysCondition:
            case WaterLogDaysCondition:
            case TotalPointsCondition:
                var events = await GetPointEventsAsync(null, null, userId, cancellationToken);
                return conditionType switch
                {
                    MealLogDaysCondition => events.Count(e => e.Category == NutritionCategory),
                    WaterLogDaysCondition => events.Count(e => e.Category == WaterCategory),
                    // Basari puanlari haric tutulur; aksi halde bir basari baska bir basariyi tetikleyebilir.
                    _ => events.Where(e => e.Category != AchievementsCategory).Sum(e => e.Points),
                };
            default:
                return null;
        }
    }

    private async Task UpdateScoreSnapshotAsync(long userId, CancellationToken cancellationToken)
    {
        var events = await GetPointEventsAsync(null, null, userId, cancellationToken);
        var total = events.Sum(e => e.Points);
        var now = DateTime.UtcNow;

        var scoreRepo = _unitOfWork.Repository<MerfitScore>();
        var score = await scoreRepo.GetQueryable(asNoTracking: false)
            .Where(s => s.UserId == userId && s.Period == AllTimePeriodTag)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (score is null)
        {
            score = new MerfitScore { UserId = userId, Period = AllTimePeriodTag, CreatedAt = now };
            await scoreRepo.AddAsync(score, cancellationToken);
        }
        else
        {
            score.UpdatedAt = now;
            scoreRepo.Update(score);
        }

        score.Score = total;
        score.CalculatedAt = now;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var breakdownRepo = _unitOfWork.Repository<MerfitScoreBreakdown>();
        var oldBreakdown = await breakdownRepo.FindAsync(b => b.MerfitScoreId == score.Id, cancellationToken);
        breakdownRepo.RemoveRange(oldBreakdown);
        await breakdownRepo.AddRangeAsync(events
            .GroupBy(e => e.Category)
            .Select(g => new MerfitScoreBreakdown
            {
                MerfitScoreId = score.Id,
                Category = g.Key,
                Points = g.Sum(e => e.Points),
                CreatedAt = now,
            }), cancellationToken);

        // Gun basina tek gecmis kaydi tutulur (grafik ve haftalik degisim icin).
        var todayStart = GetLocalDayStartUtc(now);
        var historyRepo = _unitOfWork.Repository<MerfitScoreHistory>();
        var todayHistory = await historyRepo.GetQueryable(asNoTracking: false)
            .Where(h => h.UserId == userId && h.RecordedAt >= todayStart)
            .OrderByDescending(h => h.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (todayHistory is null)
        {
            await historyRepo.AddAsync(new MerfitScoreHistory { UserId = userId, Score = total, RecordedAt = now, CreatedAt = now }, cancellationToken);
        }
        else
        {
            todayHistory.Score = total;
            todayHistory.RecordedAt = now;
            todayHistory.UpdatedAt = now;
            historyRepo.Update(todayHistory);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private DateTime LocalDate(DateTime utc) => (utc + Offset).Date;

    private IEnumerable<MerfitPointEvent> FirstPerDay(IEnumerable<(long UserId, DateTime At)> logs, string category, int points) =>
        logs
            .GroupBy(l => new { l.UserId, Day = LocalDate(l.At) })
            .Select(g => new MerfitPointEvent(g.Key.UserId, g.Min(l => l.At), category, points));
}
