using System.Globalization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Energy;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Energy;

/// <summary>
/// IEnergyBalanceService'in varsayilan implementasyonu. Veriyi sabit sayida toplu sorguyla okur ve her gunu
/// <see cref="EnergyBalanceCalculator"/> ile hesaplar (gun ve aralik ayni kod yolundan gecer).
///
/// Gun siniri, takvimle (ActivityCalendarService) aynidir: ogunler istemcinin sectigi yerel gune (Meal.Date),
/// adimlar cihazin yerel gunune (DailyActivity.Date), antrenmanlar CompletedAt'in kullanici saat dilimindeki gunune
/// atanir. Kilo, o gune ait ya da oncesindeki en yakin tartimdan alinir; tartim yoksa profil kilosu kullanilir.
/// </summary>
public class EnergyBalanceService : IEnergyBalanceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INutritionCalculator _nutritionCalculator;
    private readonly EnergyBalanceOptions _options;
    private readonly NutritionCalculationOptions _nutritionOptions;
    private readonly NotificationOptions _notificationOptions;
    private readonly TimeProvider _clock;
    private readonly EnergyBalanceCalculator _calculator;

    public EnergyBalanceService(
        IUnitOfWork unitOfWork,
        INutritionCalculator nutritionCalculator,
        IOptions<EnergyBalanceOptions> options,
        IOptions<NutritionCalculationOptions> nutritionOptions,
        IOptions<NotificationOptions>? notificationOptions = null,
        TimeProvider? clock = null)
    {
        _unitOfWork = unitOfWork;
        _nutritionCalculator = nutritionCalculator;
        _options = options.Value;
        _nutritionOptions = nutritionOptions.Value;
        _notificationOptions = notificationOptions?.Value ?? new NotificationOptions();
        _clock = clock ?? TimeProvider.System;
        _calculator = new EnergyBalanceCalculator(_options, _nutritionOptions);
    }

    public async Task<EnergyDayDto> GetDayAsync(long userId, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var clock = await ResolveClockAsync(userId, cancellationToken);
        var day = date ?? clock.Today;
        if (day > clock.Today)
        {
            throw new AppValidationException("date", "Gelecekteki bir gün için kalori dengesi hesaplanamaz.");
        }

        var context = await LoadAsync(userId, day, day, clock, cancellationToken);
        return _calculator.Calculate(context.BuildInput(day));
    }

    public async Task<EnergyRangeResponse> GetRangeAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new AppValidationException("from", "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        if (to.DayNumber - from.DayNumber + 1 > _options.MaxRangeDays)
        {
            throw new AppValidationException("to", $"En fazla {_options.MaxRangeDays} günlük aralık sorgulanabilir.");
        }

        var clock = await ResolveClockAsync(userId, cancellationToken);
        if (to > clock.Today)
        {
            to = clock.Today;
        }

        if (from > to)
        {
            throw new AppValidationException("from", "Gelecekteki bir aralık için kalori dengesi hesaplanamaz.");
        }

        var context = await LoadAsync(userId, from, to, clock, cancellationToken);
        var days = new List<EnergyDayDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            days.Add(_calculator.Calculate(context.BuildInput(d)));
        }

        var response = new EnergyRangeResponse
        {
            From = from,
            To = to,
            Today = clock.Today,
            TimeZone = clock.Zone.Id,
            TargetCalories = context.TargetCalories,
            GoalDirection = context.GoalDirection,
            Days = days.Select(d => new EnergyRangeDayDto
            {
                Date = d.Date,
                IsToday = d.IsToday,
                IntakeCalories = d.IntakeCalories,
                ExpenditureCalories = d.ExpenditureCalories,
                Balance = d.Balance,
                Tone = d.Tone,
                Status = d.Status,
                HasMeals = d.DataQuality.HasMeals,
                IsComplete = d.DataQuality.IsComplete,
                Steps = d.Activity.Steps,
                WorkoutCalories = d.Activity.WorkoutCalories,
            }).ToList(),
        };

        response.Summary = BuildSummary(days, context, from, to);
        response.Weeks = BuildWeeks(days, from, to);
        return response;
    }

    private EnergyRangeSummaryDto BuildSummary(List<EnergyDayDto> days, LoadedContext context, DateOnly from, DateOnly to)
    {
        var complete = days.Where(d => d.DataQuality.IsComplete).ToList();
        var summary = new EnergyRangeSummaryDto { TotalDays = days.Count, CompleteDays = complete.Count };
        if (complete.Count > 0)
        {
            summary.AverageIntake = (int)Math.Round(complete.Average(d => d.IntakeCalories));
            summary.AverageExpenditure = (int)Math.Round(complete.Average(d => d.ExpenditureCalories!.Value));
            summary.AverageBalance = (int)Math.Round(complete.Average(d => d.Balance!.Value));
            summary.TotalBalance = complete.Sum(d => d.Balance!.Value);
            summary.EstimatedWeightChangeKg = Math.Round((decimal)summary.TotalBalance.Value / _options.KcalPerKg, 2);
        }

        summary.Weight = context.WeightTrend(from, to);
        summary.Analysis = BuildAnalysis(summary);
        return summary;
    }

    private static string? BuildAnalysis(EnergyRangeSummaryDto summary)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        if (summary.CompleteDays < 3 || summary.AverageBalance is null)
        {
            return "Dönemi yorumlamak için en az 3 gün besin kaydı gerekir.";
        }

        var average = summary.AverageBalance.Value;
        var kind = average > 0 ? "fazla" : "açık";
        var text = $"Kayıtlı {summary.CompleteDays} günde ortalama günlük {Math.Abs(average).ToString("N0", tr)} kcal {kind}; " +
            $"bu yaklaşık {summary.EstimatedWeightChangeKg!.Value.ToString("+0.0;-0.0;0.0", tr)} kg değişime karşılık gelir.";
        if (summary.Weight.ChangeKg is { } change && summary.Weight.MeasurementCount >= 2)
        {
            text += $" Tartımların {change.ToString("+0.0;-0.0;0.0", tr)} kg değişim gösteriyor. Kısa dönemde su ve glikojen değişimleri tartıyı etkileyebilir.";
        }
        else
        {
            text += " Gerçek kilo değişimiyle karşılaştırmak için dönemde en az iki tartım gerekir.";
        }

        return text;
    }

    private static List<EnergyWeekDto> BuildWeeks(List<EnergyDayDto> days, DateOnly from, DateOnly to)
    {
        var weeks = new List<EnergyWeekDto>();
        for (var end = to; end >= from; end = end.AddDays(-7))
        {
            var start = end.AddDays(-6) < from ? from : end.AddDays(-6);
            var complete = days.Where(d => d.Date >= start && d.Date <= end && d.DataQuality.IsComplete).ToList();
            weeks.Add(new EnergyWeekDto
            {
                Start = start,
                End = end,
                CompleteDays = complete.Count,
                AverageBalance = complete.Count > 0 ? (int)Math.Round(complete.Average(d => d.Balance!.Value)) : null,
                AverageIntake = complete.Count > 0 ? (int)Math.Round(complete.Average(d => d.IntakeCalories)) : null,
                AverageExpenditure = complete.Count > 0 ? (int)Math.Round(complete.Average(d => d.ExpenditureCalories!.Value)) : null,
            });
        }

        weeks.Reverse();
        return weeks;
    }

    private sealed record UserClock(TimeZoneInfo Zone, DateTime NowUtc, DateOnly Today);

    private async Task<UserClock> ResolveClockAsync(long userId, CancellationToken cancellationToken)
    {
        var preference = await _unitOfWork.Repository<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var zone = NotificationClock.Resolve(preference?.TimeZone, _notificationOptions);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        return new UserClock(zone, nowUtc, DateOnly.FromDateTime(NotificationClock.ToLocal(nowUtc, zone)));
    }

    private async Task<LoadedContext> LoadAsync(long userId, DateOnly from, DateOnly to, UserClock clock, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var goal = await _unitOfWork.Repository<NutritionGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);

        // ---- Besinler (Meal.Date yerel gunun UTC gece yarisi olarak saklanir) ----
        var mealStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var mealEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var meals = await (
                from item in _unitOfWork.Repository<MealItem>().GetQueryable()
                join meal in _unitOfWork.Repository<Meal>().GetQueryable() on item.MealId equals meal.Id
                where meal.UserId == userId && meal.Date >= mealStart && meal.Date < mealEnd
                select new { meal.Date, meal.MealType, item.Calories, item.Protein, item.Carbs, item.Fat })
            .ToListAsync(cancellationToken);
        var mealsByDay = meals.ToLookup(
            m => DateOnly.FromDateTime(m.Date),
            m => new EnergyMealEntry(m.MealType, m.Calories, m.Protein, m.Carbs, m.Fat));

        // ---- Adimlar ----
        var stepRows = await _unitOfWork.Repository<DailyActivity>().GetQueryable()
            .Where(a => a.UserId == userId && a.Date >= from && a.Date <= to)
            .ToListAsync(cancellationToken);
        var stepsByDay = stepRows.ToDictionary(
            a => a.Date,
            a => new EnergyStepDay(a.TotalSteps, a.DistanceMeters, a.CaloriesBurned, a.HourlySteps ?? Array.Empty<int>(),
                a.UtcOffsetMinutes, a.IsSuspicious, a.LastSyncedAt));

        // ---- Kilo (o gune ait ya da onceki en yakin tartim) ----
        var weightEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var inRangeWeights = await _unitOfWork.Repository<BodyMeasurement>().GetQueryable()
            .Where(m => m.UserId == userId && m.WeightKg != null && m.RecordedAt >= mealStart && m.RecordedAt < weightEnd)
            .Select(m => new { m.RecordedAt, m.WeightKg, m.Id })
            .ToListAsync(cancellationToken);
        var previousWeight = await _unitOfWork.Repository<BodyMeasurement>().GetQueryable()
            .Where(m => m.UserId == userId && m.WeightKg != null && m.RecordedAt < mealStart)
            .OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id)
            .Select(m => new { m.RecordedAt, m.WeightKg, m.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (previousWeight is not null)
        {
            inRangeWeights.Add(previousWeight);
        }

        var weights = inRangeWeights
            .OrderBy(w => w.RecordedAt).ThenBy(w => w.Id)
            .Select(w => (Date: DateOnly.FromDateTime(w.RecordedAt), Kg: w.WeightKg!.Value))
            .ToList();

        // ---- Antrenmanlar ----
        var (rangeStartUtc, _) = NotificationClock.LocalDayBoundsUtc(from, clock.Zone);
        var (_, rangeEndUtc) = NotificationClock.LocalDayBoundsUtc(to, clock.Zone);
        var sessions = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.UserId == userId
                && s.Status == WorkoutSessionStatus.Completed
                && s.CompletedAt != null
                && s.CompletedAt >= rangeStartUtc
                && s.CompletedAt < rangeEndUtc)
            .Select(s => new { s.Id, s.WorkoutId, s.WorkoutPlanDayId, s.StartedAt, CompletedAt = s.CompletedAt!.Value, s.DurationSeconds, s.CaloriesBurned })
            .ToListAsync(cancellationToken);
        var workoutProfiles = await WorkoutCalorieEstimator.LoadProfilesAsync(
            _unitOfWork, sessions.Where(s => s.WorkoutId.HasValue).Select(s => s.WorkoutId!.Value).Distinct().ToList(), cancellationToken);
        var sessionTitles = await WorkoutSessions.WorkoutSessionTitles.LoadAsync(
            _unitOfWork, sessions.Select(s => (s.WorkoutId, s.WorkoutPlanDayId)), cancellationToken);

        var context = new LoadedContext(clock, profile, mealsByDay, stepsByDay, weights);
        foreach (var s in sessions)
        {
            var day = DateOnly.FromDateTime(NotificationClock.ToLocal(s.CompletedAt, clock.Zone));
            WorkoutEnergyProfile? meta = null;
            if (s.WorkoutId is long workoutId)
            {
                workoutProfiles.TryGetValue(workoutId, out meta);
            }
            else if (s.WorkoutPlanDayId.HasValue)
            {
                meta = WorkoutEnergyProfile.ForEngineSession(sessionTitles.Resolve(null, s.WorkoutPlanDayId));
            }
            var active = WorkoutCalorieEstimator.ActiveSeconds(s.DurationSeconds, s.StartedAt, s.CompletedAt, _options.MaxWorkoutMinutes);
            // Saklanan deger tek kaynaktir (oturum tamamlanirken sunucuda hesaplanir). Eski/eksik kayitlarda ayni
            // formulle o gunun kilosundan hesaplanir; o da mumkun degilse antrenman kaloriye katilmaz.
            var calories = s.CaloriesBurned is { } stored && stored >= 0
                ? (int?)Math.Round(stored, 0, MidpointRounding.AwayFromZero)
                : meta is null ? null : WorkoutCalorieEstimator.EstimateNetKcal(meta.Kind, meta.Difficulty, context.WeightAt(day).Kg, active);
            context.AddWorkout(day, new EnergyWorkoutEntry(
                s.Id, meta?.Title ?? string.Empty, meta?.Kind ?? WorkoutEnergyKind.Strength, s.CompletedAt, active, calories));
        }

        // ---- Hedef ----
        var evaluation = profile is null ? null : _nutritionCalculator.Evaluate(profile);
        var calc = evaluation?.Result;
        context.TargetCalories = goal is { DailyCalories: > 0 } ? (int)Math.Round(goal.DailyCalories)
            : calc is not null ? (int)Math.Round(calc.DailyCalories) : null;
        context.ProteinTarget = goal is { ProteinTarget: > 0 } ? (int)Math.Round(goal.ProteinTarget) : calc is null ? null : (int)Math.Round(calc.ProteinTarget);
        context.CarbsTarget = goal is { CarbsTarget: > 0 } ? (int)Math.Round(goal.CarbsTarget) : calc is null ? null : (int)Math.Round(calc.CarbsTarget);
        context.FatTarget = goal is { FatTarget: > 0 } ? (int)Math.Round(goal.FatTarget) : calc is null ? null : (int)Math.Round(calc.FatTarget);
        context.PlannedBalance = calc is not null && context.TargetCalories is { } target ? target - (int)Math.Round(calc.Tdee) : null;
        context.GoalDirection = ResolveDirection(profile?.Goal, calc?.GoalCalorieAdjustment);
        return context;
    }

    /// <summary>
    /// Hedef yonu: hesaplanan hedef duzeltmesinin isaretinden (acik = kilo vermek, fazla = kilo almak, 0 = korumak).
    /// Profil eksikse secilen hedeften: Yag Kaybi -> kilo vermek, Kas Gelistirme -> kilo almak, Guclenme -> korumak.
    /// </summary>
    public static string ResolveDirection(FitnessGoal? goal, decimal? adjustment)
    {
        if (adjustment is { } a)
        {
            return a < 0 ? "lose" : a > 0 ? "gain" : "maintain";
        }

        if (goal is null)
        {
            return "unknown";
        }

        return FitnessGoals.Normalize(goal.Value) switch
        {
            FitnessGoal.LoseWeight => "lose",
            FitnessGoal.BuildMuscle => "gain",
            _ => "maintain",
        };
    }

    private sealed class LoadedContext
    {
        private readonly UserClock _clock;
        private readonly UserProfile? _profile;
        private readonly ILookup<DateOnly, EnergyMealEntry> _meals;
        private readonly Dictionary<DateOnly, EnergyStepDay> _steps;
        private readonly List<(DateOnly Date, decimal Kg)> _weights;
        private readonly Dictionary<DateOnly, List<EnergyWorkoutEntry>> _workouts = new();

        public LoadedContext(UserClock clock, UserProfile? profile, ILookup<DateOnly, EnergyMealEntry> meals,
            Dictionary<DateOnly, EnergyStepDay> steps, List<(DateOnly Date, decimal Kg)> weights)
        {
            _clock = clock;
            _profile = profile;
            _meals = meals;
            _steps = steps;
            _weights = weights;
        }

        public int? TargetCalories { get; set; }
        public int? ProteinTarget { get; set; }
        public int? CarbsTarget { get; set; }
        public int? FatTarget { get; set; }
        public int? PlannedBalance { get; set; }
        public string GoalDirection { get; set; } = "unknown";

        public void AddWorkout(DateOnly day, EnergyWorkoutEntry entry)
        {
            if (!_workouts.TryGetValue(day, out var list))
            {
                _workouts[day] = list = new List<EnergyWorkoutEntry>();
            }

            list.Add(entry);
        }

        public (decimal? Kg, bool FromMeasurement) WeightAt(DateOnly day)
        {
            for (var i = _weights.Count - 1; i >= 0; i--)
            {
                if (_weights[i].Date <= day)
                {
                    return (_weights[i].Kg, true);
                }
            }

            return (_profile?.WeightKg, false);
        }

        public EnergyWeightTrendDto WeightTrend(DateOnly from, DateOnly to)
        {
            var inRange = _weights.Where(w => w.Date >= from && w.Date <= to).ToList();
            var baseline = _weights.LastOrDefault(w => w.Date < from);
            var points = baseline != default ? new[] { baseline }.Concat(inRange).ToList() : inRange;
            var trend = new EnergyWeightTrendDto { MeasurementCount = points.Count };
            if (points.Count > 0)
            {
                trend.StartDate = points[0].Date;
                trend.StartKg = points[0].Kg;
                trend.EndDate = points[^1].Date;
                trend.EndKg = points[^1].Kg;
                trend.ChangeKg = points.Count >= 2 ? Math.Round(points[^1].Kg - points[0].Kg, 1) : null;
            }

            return trend;
        }

        public EnergyDayInput BuildInput(DateOnly day)
        {
            var isToday = day == _clock.Today;
            var fraction = 1d;
            if (isToday)
            {
                var (start, end) = NotificationClock.LocalDayBoundsUtc(day, _clock.Zone);
                fraction = Math.Clamp((_clock.NowUtc - start).TotalMinutes / (end - start).TotalMinutes, 0, 1);
            }

            var (weight, fromMeasurement) = WeightAt(day);
            return new EnergyDayInput
            {
                Date = day,
                IsToday = isToday,
                ElapsedDayFraction = fraction,
                NowUtc = _clock.NowUtc,
                WeightKg = weight,
                WeightFromMeasurement = fromMeasurement,
                HeightCm = _profile?.HeightCm,
                Age = _profile?.DateOfBirth is { } dob ? AgeAt(dob, day) : null,
                Gender = _profile?.Gender,
                Meals = _meals[day].ToList(),
                Steps = _steps.GetValueOrDefault(day),
                Workouts = _workouts.GetValueOrDefault(day) ?? new List<EnergyWorkoutEntry>(),
                TargetCalories = TargetCalories,
                ProteinTarget = ProteinTarget,
                CarbsTarget = CarbsTarget,
                FatTarget = FatTarget,
                PlannedBalance = PlannedBalance,
                GoalDirection = GoalDirection,
            };
        }

        private static int AgeAt(DateTime dateOfBirth, DateOnly day)
        {
            var age = day.Year - dateOfBirth.Year;
            if (day < DateOnly.FromDateTime(dateOfBirth).AddYears(age))
            {
                age--;
            }

            return age;
        }
    }
}
